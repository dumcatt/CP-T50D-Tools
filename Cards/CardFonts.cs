using System.Drawing.Text;

namespace CPT50DTools.Cards;

/// <summary>
/// Loads the game's own fonts from data\graphics\font (plain TrueType files, installed by
/// soundvoltex.dll at startup) so card text is drawn in the arcade typeface.
/// Printed genesis cards use DFPMaruGothic-SB (font file 0003) for the illustrator, title and serial.
/// </summary>
public static class CardFonts
{
    /// <summary>The face printed cards use, from data\graphics\font\0003.</summary>
    public const string CardFace = "DFPMaruGothic-SB";

    // Fallbacks for when no game data folder is loaded.
    private static readonly string[] Fallbacks = { "DFPMaruGothic-SB", "DFPHSGothic-W7", "Yu Gothic UI", "Meiryo", "MS Gothic", "Segoe UI" };

    private static readonly object _lock = new();
    private static PrivateFontCollection? _collection;
    private static string? _folder;
    private static FontFamily? _cardFamily;

    /// <summary>Loads every font in the game's data\graphics\font folder (null = use installed fonts only).</summary>
    public static void SetDataFolder(string? dataFolder)
    {
        string? folder = dataFolder != null ? Path.Combine(dataFolder, "graphics", "font") : null;
        lock (_lock)
        {
            if (string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase)) return;
            _folder = folder;
            _cardFamily = null;
            var old = _collection;
            _collection = null;

            if (folder != null && Directory.Exists(folder))
            {
                var pfc = new PrivateFontCollection();
                foreach (var file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    try { pfc.AddFontFile(file); } catch { /* not a font */ }
                }
                _collection = pfc;
            }
            old?.Dispose();
        }
    }

    /// <summary>Names of the fonts loaded from the game folder (for logging).</summary>
    public static IReadOnlyList<string> LoadedFamilies
    {
        get { lock (_lock) return _collection?.Families.Select(f => f.Name).ToList() ?? new List<string>(); }
    }

    /// <summary>True when the card face came from the game files rather than a fallback.</summary>
    public static bool UsingGameFont
    {
        get { lock (_lock) return CardFamilyLocked().Name == CardFace && _collection?.Families.Any(f => f.Name == CardFace) == true; }
    }

    /// <summary>Creates a font in the card typeface. Caller disposes it.</summary>
    public static Font Create(float pixelSize)
    {
        lock (_lock)
        {
            var family = CardFamilyLocked();
            var style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
            // Fallback faces are lighter than DF Maru Gothic SB, so embolden them.
            if (family.Name != CardFace && family.IsStyleAvailable(FontStyle.Bold) && !family.Name.StartsWith("DF")) style = FontStyle.Bold;
            return new Font(family, pixelSize, style, GraphicsUnit.Pixel);
        }
    }

    private static FontFamily CardFamilyLocked()
    {
        if (_cardFamily != null) return _cardFamily;
        _cardFamily = _collection?.Families.FirstOrDefault(f => f.Name == CardFace);
        if (_cardFamily == null)
        {
            var installed = FontFamily.Families.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
            _cardFamily = Fallbacks.Select(n => installed.GetValueOrDefault(n)).FirstOrDefault(f => f != null) ?? FontFamily.GenericSansSerif;
        }
        return _cardFamily;
    }
}
