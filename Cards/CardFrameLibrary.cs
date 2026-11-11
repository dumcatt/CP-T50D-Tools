using System.Reflection;
using CPT50DTools.Ifs;

namespace CPT50DTools.Cards;

/// <summary>
/// Supplies the card frame, plaque, "ILLUST" tag and copyright textures.
/// Textures are read from the game's own graphics\verNN\card_data.ifs (each game version has its
/// own plaque artwork and logo), falling back to the bundled Resources\Frames copies (v3).
/// </summary>
public sealed class CardFrameLibrary
{
    private static readonly Lazy<CardFrameLibrary> _lazy = new(() => new CardFrameLibrary());
    public static CardFrameLibrary Instance => _lazy.Value;

    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, IfsArchive?> _archives = new();
    private readonly Assembly _asm = typeof(CardFrameLibrary).Assembly;
    private readonly object _lock = new();
    private string? _dataFolder;

    /// <summary>Sets the game "data" folder textures are read from (null = bundled textures only).</summary>
    public void SetDataFolder(string? dataFolder)
    {
        lock (_lock)
        {
            if (string.Equals(_dataFolder, dataFolder, StringComparison.OrdinalIgnoreCase)) return;
            _dataFolder = dataFolder;
            foreach (var b in _cache.Values) b?.Dispose();
            _cache.Clear();
            _archives.Clear();
        }
    }

    /// <summary>
    /// Returns a texture for the given game version. Bitmaps are shared and cached: callers must
    /// not dispose them, and must only draw them while holding <see cref="SyncRoot"/>.
    /// </summary>
    public Bitmap? Get(int version, string name)
    {
        lock (_lock)
        {
            string key = $"{version}/{name}";
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var bmp = LoadFromGame(version, name) ?? LoadBundled(name);
            _cache[key] = bmp;
            return bmp;
        }
    }

    /// <summary>GDI+ bitmaps are not thread safe; hold this while drawing textures from this library.</summary>
    public object SyncRoot => _lock;

    private Bitmap? LoadFromGame(int version, string name)
    {
        if (_dataFolder == null) return null;
        // Prefer the card's own version, then older versions, then newer ones.
        var versions = Enumerable.Range(1, version).Reverse().Concat(Enumerable.Range(version + 1, 10));
        foreach (int v in versions)
        {
            var archive = OpenArchive(v);
            var img = archive?.Images.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            if (img == null) continue;
            try { return archive!.DecodeImage(img); } catch { }
        }
        return null;
    }

    private IfsArchive? OpenArchive(int version)
    {
        if (_archives.TryGetValue(version, out var a)) return a;
        string path = Path.Combine(_dataFolder!, "graphics", $"ver{version:D2}", "card_data.ifs");
        try { a = File.Exists(path) ? IfsArchive.Open(path) : null; } catch { a = null; }
        _archives[version] = a;
        return a;
    }

    private Bitmap? LoadBundled(string name)
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Frames", name + ".png");
        try
        {
            if (File.Exists(filePath))
            {
                using var img = Image.FromFile(filePath);
                return new Bitmap(img);
            }
            using var stream = _asm.GetManifestResourceStream($"CPT50DTools.Resources.Frames.{name}.png");
            if (stream != null)
            {
                using var img = Image.FromStream(stream);
                return new Bitmap(img);
            }
        }
        catch { }
        return null;
    }

}
