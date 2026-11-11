using System.Drawing.Imaging;
using System.Reflection;

namespace CPT50DTools.Cards;

/// <summary>
/// Manages high-resolution, vector-grade card frame overlays, banners, crests, and copyright logos.
/// Loads directly from embedded resources or the local Resources\Frames directory.
/// </summary>
public sealed class CardFrameLibrary : IDisposable
{
    private static readonly Lazy<CardFrameLibrary> _lazy = new(() => new CardFrameLibrary());
    public static CardFrameLibrary Instance => _lazy.Value;

    private readonly Dictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Assembly _asm = typeof(CardFrameLibrary).Assembly;

    public Bitmap? GetFrame(FrameStyle style) => style switch
    {
        FrameStyle.None => null,
        FrameStyle.Gold => LoadTexture("frame_gold"),
        FrameStyle.Silver => LoadTexture("frame_sil"),
        FrameStyle.Legend => LoadTexture("frame_lg01"),
        FrameStyle.Touhou => LoadTexture("frame_toho01"),
        FrameStyle.White => LoadTexture("frame_white2"),
        FrameStyle.Museca => LoadTexture("frame_museca"),
        _ => LoadTexture("frame_gold")
    };

    public Bitmap? GetPlaque(PlaqueStyle style) => style switch
    {
        PlaqueStyle.Gold => LoadTexture("frame_gold_name"),
        PlaqueStyle.Silver => LoadTexture("frame_sil_name"),
        PlaqueStyle.Touhou => LoadTexture("frame_toho_name"),
        _ => LoadTexture("frame_gold_name")
    };

    public Bitmap? GetPlaque(FrameStyle style) => style switch
    {
        FrameStyle.None => null,
        FrameStyle.Gold or FrameStyle.Legend => LoadTexture("frame_gold_name"),
        FrameStyle.Silver or FrameStyle.White => LoadTexture("frame_sil_name"),
        FrameStyle.Touhou => LoadTexture("frame_toho_name"),
        _ => LoadTexture("frame_gold_name")
    };

    public Bitmap? GetMidashi(int version) => version switch
    {
        3 => LoadTexture("illust_midashi_v3") ?? LoadTexture("illust_midashi"),
        4 => LoadTexture("illust_midashi_v4") ?? LoadTexture("illust_midashi"),
        5 => LoadTexture("illust_midashi_v5") ?? LoadTexture("illust_midashi"),
        _ => LoadTexture("illust_midashi_v3") ?? LoadTexture("illust_midashi")
    };

    public Bitmap? GetWhiteMatte() => LoadTexture("frame_white2");

    public Bitmap? GetCopyright(int copyrightId) => copyrightId switch
    {
        1 => LoadTexture("c_konami_de"),
        2 => LoadTexture("c_toho"),
        3 => LoadTexture("c_internet"),
        4 => LoadTexture("c_exit"),
        5 => LoadTexture("c_mixi"),
        _ => LoadTexture("c_konami_de")
    };

    private Bitmap? LoadTexture(string name)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(name, out var cached)) return cached;

            // 1. Try file from output directory
            string filePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Frames", name + ".png");
            if (File.Exists(filePath))
            {
                try
                {
                    var bmp = (Bitmap)Image.FromFile(filePath);
                    _cache[name] = bmp;
                    return bmp;
                }
                catch { }
            }

            // 2. Try embedded resource
            string resName = $"CPT50DTools.Resources.Frames.{name}.png";
            using var stream = _asm.GetManifestResourceStream(resName);
            if (stream != null)
            {
                try
                {
                    using var img = Image.FromStream(stream);
                    var bmp = new Bitmap(img);
                    _cache[name] = bmp;
                    return bmp;
                }
                catch { }
            }

            return null;
        }
    }

    public void Dispose()
    {
        lock (_cache)
        {
            foreach (var b in _cache.Values) b.Dispose();
            _cache.Clear();
        }
    }
}
