using System.Text.Json;

namespace CPT50DTools;

/// <summary>User settings persisted to %AppData%\CPT50DTools\settings.json.</summary>
public sealed class AppSettings
{
    public string? LastCardFolder { get; set; }
    public string? LutOverlayFolder { get; set; }
    public string? PrinterSdkFolder { get; set; }

    public int Overcoat { get; set; }
    public int Density { get; set; }
    public int FitMode { get; set; }
    public bool Preheat { get; set; }

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
