using System.Text.Json;
using System.Text.Json.Serialization;

namespace WdiCheck.Core.Settings;

/// <summary>
/// User settings stored as JSON under %LOCALAPPDATA%\WdiCheck, which works identically
/// for the packaged (MSIX, virtualised to the package's LocalCache) and unpackaged builds.
/// </summary>
public sealed class AppSettings
{
    public string? BaseUrl { get; set; }

    /// <summary>Board chosen by the user when automatic matching was ambiguous or wrong.</summary>
    public int? BoardOverrideId { get; set; }

    /// <summary>Graphics card model chosen by the user, by the card's PCI ID (see GraphicsCard.Key).</summary>
    public Dictionary<string, int> CardOverrides { get; set; } = [];

    public bool ShowInboxAndUnmatched { get; set; }

    /// <summary>Ask Windows Update which drivers it offers this machine after each scan.</summary>
    public bool CheckWindowsUpdate { get; set; } = true;

    /// <summary>"Default", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "Default";

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WdiCheck");

    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.AppSettings) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings fall back to defaults rather than blocking startup.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
