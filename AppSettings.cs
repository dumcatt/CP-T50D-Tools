using System.Text.Json;
using System.Text.Json.Serialization;

namespace CPT50DTools;

/// <summary>User settings persisted to %AppData%\CPT50DTools\settings.json.</summary>
public sealed class AppSettings
{
    public string? LastCardFolder { get; set; }
    /// <summary>
    /// The game's ext\cpusbxpkm_data folder (LUTs, compensation tables, KMAT overlays), found when a
    /// game folder is loaded. Stored under its old name so existing settings files keep it.
    /// </summary>
    [JsonPropertyName("LutOverlayFolder")]
    public string? PrinterDataFolder { get; set; }
    public string? PrinterSdkFolder { get; set; }

    public int Overcoat { get; set; }
    public int Density { get; set; }
    public bool Preheat { get; set; }
    /// <summary>Pixels the card is moved down on the print to match where this printer cuts it.</summary>
    public int PrintOffsetY { get; set; } = 14;
    /// <summary>Show the print bleed around the card face in the preview.</summary>
    public bool ShowBleed { get; set; }
    /// <summary>Leave the "©year Konami Amusement" line off the card.</summary>
    public bool HideCopyright { get; set; }
    /// <summary>Leave the frame, logo plaque and ILLUST tag off the card.</summary>
    public bool HideFrame { get; set; }
    /// <summary>Leave the illustrator, title and serial off the card.</summary>
    public bool HideCardText { get; set; }

    /// <summary>Collapsed state of the Card text / Print options sections.</summary>
    public bool CardTextCollapsed { get; set; }
    public bool PrintOptionsCollapsed { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CPT50DTools", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* ignore corrupt settings */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
