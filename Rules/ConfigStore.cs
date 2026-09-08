using System.Text.Json;
using System.Text.Json.Serialization;

namespace HdrToggle.Rules;

public static class ConfigStore
{
    public static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HdrToggle");

    public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options) ?? new AppConfig();
                MigrateFrameCap(config);
                return config;
            }
        }
        catch (Exception ex)
        {
            // Starting fresh means losing every profile, so keep the unreadable file
            // around instead of overwriting it on the next save.
            Logger.Log($"Failed to load config, starting fresh: {ex.Message}");
            TryBackupBadConfig();
        }
        return new AppConfig();
    }

    /// <summary>
    /// Folds a pre-existing per-display cap into the profile-wide one. The lowest rate
    /// wins: it was the cap the user cared about, and applying it everywhere is what the
    /// setting means now.
    /// </summary>
    private static void MigrateFrameCap(AppConfig config)
    {
        foreach (var profile in config.Profiles)
        {
            if (profile.LegacyRefreshRateRules is not { Count: > 0 } legacy)
                continue;

            var capped = legacy.Values.Where(hz => hz > 0).ToList();
            if (profile.FrameCapHz == 0 && capped.Count > 0)
            {
                profile.FrameCapHz = capped.Min();
                Logger.Log($"Migrated per-display frame cap for \"{profile.Name}\" to {profile.FrameCapHz} Hz on every display.");
            }
            profile.LegacyRefreshRateRules = null;
        }
    }

    private static void TryBackupBadConfig()
    {
        try
        {
            string backup = Path.Combine(ConfigDir, "config.bad.json");
            File.Copy(ConfigPath, backup, overwrite: true);
            Logger.Log($"Unreadable config copied to {backup}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not back up the unreadable config: {ex.Message}");
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save config: {ex.Message}");
        }
    }
}

public static class Logger
{
    private static readonly string LogPath = Path.Combine(ConfigStore.ConfigDir, "log.txt");
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(ConfigStore.ConfigDir);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
