using System.Text.Json;

namespace MitsubishiCncMonitor;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath =>
        Path.Combine(Application.LocalUserAppDataPath, "settings.json");

    public static AppSettings LoadOrDefault()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return AppSettings.CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return AppSettings.CreateDefault();
        }
    }

    public static void Save(AppSettings settings)
    {
        settings.Normalize();
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
