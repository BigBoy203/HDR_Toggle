using System.Runtime.InteropServices;
using System.Text;

namespace HdrToggle.Display;

/// <summary>
/// Sets NVIDIA's per-game frame rate limit — the same "Max Frame Rate" the NVIDIA
/// Control Panel writes — through NVAPI's driver settings (DRS) API.
///
/// This is the cap that works when the refresh-rate one can't: the driver paces the
/// game's own presents, so it bites with V-Sync off and in exclusive fullscreen, where
/// the display's refresh rate is only a ceiling the game is free to ignore.
///
/// The driver reads a game's profile when the game starts, so unlike the refresh-rate
/// cap this is written when the setting is *chosen*, not when the game launches, and it
/// stays on the game's driver profile until the cap is cleared. It is per-executable and
/// nothing else is touched: the same value shows up in the NVIDIA Control Panel under
/// Manage 3D settings → Program Settings, where it can be inspected or removed by hand.
///
/// Everything here degrades to "unavailable" with a reason rather than throwing: no
/// NVIDIA driver, an entry point that moved, a struct the driver won't accept (NVAPI
/// validates the size in every version field, so a wrong layout is rejected, never
/// misread) — all of it comes back as a status code the caller can show.
/// </summary>
public static class NvidiaFrameLimiter
{
    /// <summary>"Max Frame Rate" in the control panel; 0 turns the limiter off.</summary>
    private const uint FrameRateLimitSetting = 0x10834FEE;

    private const int NvApiOk = 0;

    // NVAPI is reached through one export that hands out function pointers by id.
    private const uint IdInitialize = 0x0150E828;
    private const uint IdGetErrorMessage = 0x6C2D048C;
    private const uint IdCreateSession = 0x0694D52E;
    private const uint IdDestroySession = 0xDAD9CFF8;
    private const uint IdLoadSettings = 0x375DBD6B;
    private const uint IdSaveSettings = 0xFCBC7E14;
    private const uint IdCreateProfile = 0xCC176068;
    private const uint IdCreateApplication = 0x4347A9DE;
    private const uint IdFindApplicationByName = 0xEEE566B2;
    private const uint IdSetSetting = 0x577DD202;
    private const uint IdGetSetting = 0x73BF8338;
    private const uint IdDeleteProfileSetting = 0xE4A26362;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr QueryInterfaceFn(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NoArgsFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ErrorMessageFn(int status, byte[] description);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CreateSessionFn(out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SessionFn(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FindApplicationFn(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr application);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CreateProfileFn(IntPtr session, IntPtr profileInfo, out IntPtr profile);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProfileBufferFn(IntPtr session, IntPtr profile, IntPtr buffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint settingId);

    // ---- NVAPI struct sizes and field offsets ------------------------------------
    // Written into fixed-size byte buffers rather than modelled as structs: every one of
    // these carries unions of 4 KB strings, and the version field is (size | version<<16),
    // so the sizes below are what makes the driver accept the call at all.

    private const int UnicodeStringSize = 2048 * 2;      // NvAPI_UnicodeString: NvU16[2048]
    private const int BinarySettingSize = 4 + 4096;      // NVDRS_BINARY_SETTING, the widest union member

    // NVDRS_SETTING_V1
    private const int SettingNameOffset = 4;
    private const int SettingIdOffset = SettingNameOffset + UnicodeStringSize;
    private const int SettingTypeOffset = SettingIdOffset + 4;
    private const int SettingLocationOffset = SettingTypeOffset + 4;
    private const int SettingIsCurrentPredefinedOffset = SettingLocationOffset + 4;
    private const int SettingIsPredefinedValidOffset = SettingIsCurrentPredefinedOffset + 4;
    private const int SettingPredefinedValueOffset = SettingIsPredefinedValidOffset + 4;
    private const int SettingCurrentValueOffset = SettingPredefinedValueOffset + BinarySettingSize;
    private const int SettingSize = SettingCurrentValueOffset + BinarySettingSize;

    // NVDRS_PROFILE_V1
    private const int ProfileNameOffset = 4;
    private const int ProfileGpuSupportOffset = ProfileNameOffset + UnicodeStringSize;
    private const int ProfileSize = ProfileGpuSupportOffset + 16;

    // NVDRS_APPLICATION_V1
    private const int ApplicationNameOffset = 8;
    private const int ApplicationFriendlyNameOffset = ApplicationNameOffset + UnicodeStringSize;
    private const int ApplicationLauncherOffset = ApplicationFriendlyNameOffset + UnicodeStringSize;
    private const int ApplicationSize = ApplicationLauncherOffset + UnicodeStringSize;

    private const uint AllGpuTypes = 0x7; // NVDRS_GPU_SUPPORT: geforce | quadro | nvs

    private static uint Version(int size, int version) => (uint)size | ((uint)version << 16);

    private static readonly object Gate = new();

    private static bool _probed;
    private static string? _unavailable;
    private static NoArgsFn? _initialize;
    private static ErrorMessageFn? _errorMessage;
    private static CreateSessionFn? _createSession;
    private static SessionFn? _destroySession;
    private static SessionFn? _loadSettings;
    private static SessionFn? _saveSettings;
    private static CreateProfileFn? _createProfile;
    private static ProfileBufferFn? _createApplication;
    private static FindApplicationFn? _findApplication;
    private static ProfileBufferFn? _setSetting;
    private static GetSettingFn? _getSetting;
    private static DeleteSettingFn? _deleteSetting;

    /// <summary>True when this machine has a driver that can take a frame rate limit.</summary>
    public static bool IsAvailable
    {
        get
        {
            lock (Gate)
                return Probe();
        }
    }

    /// <summary>Why <see cref="IsAvailable"/> is false, for the UI to show; null when it's true.</summary>
    public static string? UnavailableReason
    {
        get
        {
            lock (Gate)
            {
                Probe();
                return _unavailable;
            }
        }
    }

    /// <summary>
    /// Limits <paramref name="exeName"/> (a bare file name such as "GTAIV.exe") to
    /// <paramref name="fps"/> frames per second. Takes effect the next time the game
    /// starts — the driver reads the profile at launch — and stays until it is cleared.
    /// </summary>
    public static bool TrySetLimit(string exeName, int fps, out string message)
    {
        if (fps <= 0)
            return TryClearLimit(exeName, out message);

        return WithSession(out message, session =>
        {
            if (!TryGetProfile(session, exeName, create: true, out IntPtr profile, out string failure))
                return (false, failure);

            byte[] setting = new byte[SettingSize];
            WriteUInt32(setting, 0, Version(SettingSize, 1));
            WriteUInt32(setting, SettingIdOffset, FrameRateLimitSetting);
            WriteUInt32(setting, SettingTypeOffset, 0); // NVDRS_DWORD_TYPE
            WriteUInt32(setting, SettingCurrentValueOffset, (uint)fps);

            int status = Pinned(setting, p => _setSetting!(session, profile, p));
            if (status != NvApiOk)
                return (false, $"NVIDIA driver refused the frame rate limit: {Describe(status)}");

            status = _saveSettings!(session);
            if (status != NvApiOk)
                return (false, $"Could not save the NVIDIA profile: {Describe(status)}. "
                    + "Saving driver settings can need the app to run as administrator.");

            return (true, $"NVIDIA driver will hold {exeName} to {fps} FPS from its next launch");
        });
    }

    /// <summary>Removes the limit this app set on <paramref name="exeName"/>'s driver profile.</summary>
    public static bool TryClearLimit(string exeName, out string message)
    {
        return WithSession(out message, session =>
        {
            // No profile, or no setting on it, is the wanted state either way.
            if (!TryGetProfile(session, exeName, create: false, out IntPtr profile, out _))
                return (true, $"No NVIDIA frame rate limit set for {exeName}");

            if (_deleteSetting!(session, profile, FrameRateLimitSetting) != NvApiOk)
                return (true, $"No NVIDIA frame rate limit set for {exeName}");

            int status = _saveSettings!(session);
            if (status != NvApiOk)
                return (false, $"Could not save the NVIDIA profile: {Describe(status)}");

            return (true, $"Removed the NVIDIA frame rate limit for {exeName}");
        });
    }

    /// <summary>
    /// The limit currently on the game's driver profile, 0 for none, or null when it
    /// can't be read. Used to confirm a write actually landed.
    /// </summary>
    public static int? GetLimit(string exeName)
    {
        int? result = null;
        WithSession(out _, session =>
        {
            if (!TryGetProfile(session, exeName, create: false, out IntPtr profile, out string failure))
                return (false, failure);

            byte[] setting = new byte[SettingSize];
            WriteUInt32(setting, 0, Version(SettingSize, 1));
            WriteUInt32(setting, SettingIdOffset, FrameRateLimitSetting);

            // A profile with no such setting reads as "no limit", not as a failure.
            result = Pinned(setting, p => _getSetting!(session, profile, FrameRateLimitSetting, p)) == NvApiOk
                ? (int)ReadUInt32(setting, SettingCurrentValueOffset)
                : 0;
            return (true, "");
        });
        return result;
    }

    /// <summary>
    /// Finds the driver profile the executable belongs to. Games usually already have one
    /// shipped by NVIDIA, and that is the profile to write to — the same one the control
    /// panel edits. Only when there is none does this make one.
    /// </summary>
    private static bool TryGetProfile(IntPtr session, string exeName, bool create, out IntPtr profile, out string message)
    {
        profile = IntPtr.Zero;
        message = "";

        byte[] name = new byte[UnicodeStringSize];
        WriteUnicode(name, 0, exeName);
        byte[] application = new byte[ApplicationSize];
        WriteUInt32(application, 0, Version(ApplicationSize, 1));

        IntPtr found = IntPtr.Zero;
        int status = Pinned(name, namePtr => Pinned(application, appPtr =>
            _findApplication!(session, namePtr, out found, appPtr)));
        if (status == NvApiOk)
        {
            profile = found;
            return true;
        }

        if (!create)
        {
            message = $"{exeName} has no NVIDIA profile yet";
            return false;
        }

        byte[] profileInfo = new byte[ProfileSize];
        WriteUInt32(profileInfo, 0, Version(ProfileSize, 1));
        WriteUnicode(profileInfo, ProfileNameOffset, $"HDR Toggle - {exeName}");
        WriteUInt32(profileInfo, ProfileGpuSupportOffset, AllGpuTypes);

        IntPtr created = IntPtr.Zero;
        status = Pinned(profileInfo, p => _createProfile!(session, p, out created));
        if (status != NvApiOk)
        {
            message = $"Could not create an NVIDIA profile for {exeName}: {Describe(status)}";
            return false;
        }

        byte[] newApp = new byte[ApplicationSize];
        WriteUInt32(newApp, 0, Version(ApplicationSize, 1));
        WriteUnicode(newApp, ApplicationNameOffset, exeName);
        WriteUnicode(newApp, ApplicationFriendlyNameOffset, exeName);
        WriteUnicode(newApp, ApplicationLauncherOffset, "");

        status = Pinned(newApp, p => _createApplication!(session, created, p));
        if (status != NvApiOk)
        {
            message = $"Could not add {exeName} to an NVIDIA profile: {Describe(status)}";
            return false;
        }

        profile = created;
        return true;
    }

    private delegate (bool Ok, string Message) SessionWork(IntPtr session);

    /// <summary>Runs <paramref name="work"/> inside a loaded DRS session, always destroying it.</summary>
    private static bool WithSession(out string message, SessionWork work)
    {
        lock (Gate)
        {
            if (!Probe())
            {
                message = _unavailable ?? "NVIDIA driver settings are not available";
                return false;
            }

            int status = _createSession!(out IntPtr session);
            if (status != NvApiOk)
            {
                message = $"Could not open NVIDIA driver settings: {Describe(status)}";
                return false;
            }

            try
            {
                status = _loadSettings!(session);
                if (status != NvApiOk)
                {
                    message = $"Could not read NVIDIA driver settings: {Describe(status)}";
                    return false;
                }

                (bool ok, message) = work(session);
                return ok;
            }
            catch (Exception ex)
            {
                message = $"NVIDIA driver settings failed: {ex.Message}";
                return false;
            }
            finally
            {
                _destroySession!(session);
            }
        }
    }

    private static bool Probe()
    {
        if (_probed)
            return _unavailable is null;
        _probed = true;

        try
        {
            string library = Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll";
            if (!NativeLibrary.TryLoad(library, out IntPtr handle))
            {
                _unavailable = "No NVIDIA driver found on this machine";
                return false;
            }

            if (!NativeLibrary.TryGetExport(handle, "nvapi_QueryInterface", out IntPtr queryPtr))
            {
                _unavailable = "This NVIDIA driver doesn't expose the settings API";
                return false;
            }

            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(queryPtr);

            T? Resolve<T>(uint id) where T : Delegate
            {
                IntPtr fn = query(id);
                return fn == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(fn);
            }

            _initialize = Resolve<NoArgsFn>(IdInitialize);
            _errorMessage = Resolve<ErrorMessageFn>(IdGetErrorMessage);
            _createSession = Resolve<CreateSessionFn>(IdCreateSession);
            _destroySession = Resolve<SessionFn>(IdDestroySession);
            _loadSettings = Resolve<SessionFn>(IdLoadSettings);
            _saveSettings = Resolve<SessionFn>(IdSaveSettings);
            _createProfile = Resolve<CreateProfileFn>(IdCreateProfile);
            _createApplication = Resolve<ProfileBufferFn>(IdCreateApplication);
            _findApplication = Resolve<FindApplicationFn>(IdFindApplicationByName);
            _setSetting = Resolve<ProfileBufferFn>(IdSetSetting);
            _getSetting = Resolve<GetSettingFn>(IdGetSetting);
            _deleteSetting = Resolve<DeleteSettingFn>(IdDeleteProfileSetting);

            if (_initialize is null || _createSession is null || _destroySession is null || _loadSettings is null
                || _saveSettings is null || _createProfile is null || _createApplication is null
                || _findApplication is null || _setSetting is null || _getSetting is null || _deleteSetting is null)
            {
                _unavailable = "This NVIDIA driver is missing part of the settings API";
                return false;
            }

            int status = _initialize();
            if (status != NvApiOk)
            {
                _unavailable = $"NVIDIA driver would not initialise: {Describe(status)}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _unavailable = $"NVIDIA driver settings are unavailable: {ex.Message}";
            return false;
        }
    }

    /// <summary>NVAPI's own text for a status code, falling back to the raw number.</summary>
    private static string Describe(int status)
    {
        try
        {
            if (_errorMessage is not null)
            {
                byte[] text = new byte[64]; // NvAPI_ShortString
                if (_errorMessage(status, text) == NvApiOk)
                {
                    string message = Encoding.ASCII.GetString(text).TrimEnd('\0').Trim();
                    if (message.Length > 0)
                        return $"{message} ({status})";
                }
            }
        }
        catch
        {
        }
        return $"error {status}";
    }

    private static int Pinned(byte[] buffer, Func<IntPtr, int> call)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return call(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BitConverter.TryWriteBytes(buffer.AsSpan(offset), value);

    private static uint ReadUInt32(byte[] buffer, int offset) => BitConverter.ToUInt32(buffer, offset);

    /// <summary>Writes a NUL-terminated NvAPI_UnicodeString; the buffer is already zeroed.</summary>
    private static void WriteUnicode(byte[] buffer, int offset, string value)
    {
        if (Encoding.Unicode.GetByteCount(value) > UnicodeStringSize - 2) // leave the terminator
            throw new ArgumentException("Name is too long for an NVAPI string", nameof(value));
        Encoding.Unicode.GetBytes(value, 0, value.Length, buffer, offset);
    }
}
