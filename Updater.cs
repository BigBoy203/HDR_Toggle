using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using HdrToggle.Rules;

namespace HdrToggle;

/// <summary>A release on GitHub that is newer than the running build.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string? Sha256, string PageUrl, string Notes);

public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Checks the project's GitHub releases for a newer build and swaps it in for the running
/// exe. Windows won't let a running exe be overwritten, but it will let it be renamed, so
/// the current exe moves aside to <c>HdrToggle.exe.old</c>, the new one takes its name, and
/// the app restarts into it. The leftover <c>.old</c> is deleted on the next start. If the
/// exe's folder isn't writable (Program Files), the two renames are done by an elevated
/// <c>cmd</c> after a UAC prompt; the app itself never runs elevated.
/// </summary>
public static class Updater
{
    public const string Repo = "BigBoy203/HDR_Toggle";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases";
    private const string AssetName = "HdrToggle.exe";

    /// <summary>Passed to the new exe so it waits for this one to exit instead of deferring to it.</summary>
    public const string AfterUpdateArg = "--after-update";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"HdrToggle-Updater/{CurrentVersion}");
        return client;
    }

    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    private static string OldExePath => ExePath + ".old";

    /// <summary>The running build's version, e.g. 1.7.0 (build metadata stripped).</summary>
    public static Version CurrentVersion =>
        ParseVersion(Application.ProductVersion.Split('+')[0]) ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// Reads the latest published release. Returns it if it is newer than this build, or
    /// null if this build is current.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new UpdateException("No releases have been published yet.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UpdateException("GitHub is rate-limiting update checks right now. Try again in a while.");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag)
            ?? throw new UpdateException($"The latest release's tag \"{tag}\" isn't a version number.");

        Logger.Log($"Update check: latest release is {tag}, running {CurrentVersion.ToString(3)}.");
        if (version <= CurrentVersion)
            return null;

        string? url = null, sha256 = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            bool exact = name.Equals(AssetName, StringComparison.OrdinalIgnoreCase);
            if (!exact && (url is not null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                continue;

            url = asset.GetProperty("browser_download_url").GetString();
            sha256 = asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d
                     && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? d["sha256:".Length..]
                : null;
            if (exact) break;
        }
        if (url is null)
            throw new UpdateException($"Release {tag} has no {AssetName} attached to download.");

        return new UpdateInfo(version, tag, url, sha256,
            root.TryGetProperty("html_url", out var page) ? page.GetString() ?? ReleasesPage : ReleasesPage,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
    }

    /// <summary>Downloads the release's exe to a temp folder and checks it is what was published.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, CancellationToken ct)
    {
        string dir = Path.Combine(Path.GetTempPath(), "HdrToggle-update");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, AssetName);

        byte[] bytes = await Http.GetByteArrayAsync(update.DownloadUrl, ct);

        if (bytes.Length < 1024 || bytes[0] != 'M' || bytes[1] != 'Z')
            throw new UpdateException("The download isn't a Windows program. Nothing was changed.");

        if (update.Sha256 is { } expected
            && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("The download doesn't match the checksum GitHub published for it. Nothing was changed.");

        await File.WriteAllBytesAsync(file, bytes, ct);

        // A release tagged newer whose exe still says it's this version would "update"
        // forever. FileVersionInfo can't always read a single-file exe, so only a version
        // that reads back and isn't newer blocks the install.
        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            if (ParseVersion(info.ProductVersion?.Split('+')[0] ?? "") is { } built && built <= CurrentVersion)
                throw new UpdateException($"Release {update.Tag}'s exe is built as version {built.ToString(3)}, "
                    + $"which isn't newer than this one ({CurrentVersion.ToString(3)}). Nothing was changed.");
        }
        catch (UpdateException) { throw; }
        catch (Exception ex) { Logger.Log($"Could not read the downloaded exe's version: {ex.Message}"); }

        Logger.Log($"Downloaded update {update.Tag} ({bytes.Length:N0} bytes) to {file}.");
        return file;
    }

    /// <summary>
    /// Puts the downloaded exe in place of the running one. The running exe keeps working
    /// until the app exits; call <see cref="Relaunch"/> next.
    /// </summary>
    public static void Install(string downloaded)
    {
        string target = ExePath;
        string expectedHash = HashFile(downloaded);

        try
        {
            DeleteOld();
            File.Move(target, OldExePath);
            try
            {
                File.Move(downloaded, target);
            }
            catch
            {
                File.Move(OldExePath, target); // put the working exe back
                throw;
            }
        }
        catch (UnauthorizedAccessException)
        {
            InstallElevated(downloaded, target);
        }

        if (!File.Exists(target) || HashFile(target) != expectedHash)
            throw new UpdateException("The new version couldn't be put in place. The current version is still installed.");

        Logger.Log($"Installed update over {target}.");
    }

    /// <summary>The exe's folder isn't writable as the user: do the two renames elevated.</summary>
    private static void InstallElevated(string downloaded, string target)
    {
        Logger.Log($"No write access to {Path.GetDirectoryName(target)}; asking for elevation to install the update.");
        string old = OldExePath;
        string script = $"del /f /q \"{old}\" 2>nul & move /y \"{target}\" \"{old}\" >nul"
                        + $" && (move /y \"{downloaded}\" \"{target}\" >nul || move /y \"{old}\" \"{target}\" >nul)";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            }) ?? throw new UpdateException("Couldn't start the installer.");
            process.WaitForExit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC declined
        {
            throw new UpdateException("Update cancelled — administrator permission is needed to replace "
                + $"files in {Path.GetDirectoryName(target)}.");
        }
    }

    /// <summary>Starts the freshly installed exe. It waits for this instance to exit before taking over.</summary>
    public static void Relaunch()
    {
        Process.Start(new ProcessStartInfo(ExePath, AfterUpdateArg)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(ExePath) ?? "",
        });
    }

    /// <summary>
    /// Deletes the exe a previous update moved aside. The old process may take a moment to
    /// finish exiting after it lets go of the single-instance mutex, so this retries.
    /// </summary>
    public static void CleanUpAfterUpdate()
    {
        if (!File.Exists(OldExePath))
            return;
        Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    DeleteOld();
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return; // protected folder; it'll be replaced by the next update's elevated move
                }
                catch (IOException)
                {
                    await Task.Delay(1000);
                }
            }
        });
    }

    private static void DeleteOld()
    {
        if (File.Exists(OldExePath))
            File.Delete(OldExePath);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// "1.6", "v1.7.0" and "1.7.0-beta" all parse; missing parts count as zero, so the tag
    /// "1.6" equals the build version 1.6.0.
    /// </summary>
    public static Version? ParseVersion(string text)
    {
        text = text.Trim().TrimStart('v', 'V');
        int end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.'))
            end++;
        string[] parts = text[..end].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        int[] n = new int[4];
        for (int i = 0; i < Math.Min(parts.Length, 4); i++)
            if (!int.TryParse(parts[i], out n[i]))
                return null;
        return new Version(n[0], n[1], n[2], n[3]);
    }
}
