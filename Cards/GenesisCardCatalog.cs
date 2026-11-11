using System.Text;
using System.Xml.Linq;

namespace CPT50DTools.Cards;

public enum FrameStyle
{
    None,
    Gold,
    Silver,
    Legend,
    Touhou,
    White,
    Museca
}

public enum PlaqueStyle
{
    Gold,
    Silver,
    Touhou
}

public sealed class GenesisCardInfo
{
    public int Id { get; init; }
    public string TextureName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Illustrator { get; init; } = "";
    public int FrameId { get; init; } = 1;
    public int Rarity { get; init; } = 1;
    public int Version { get; init; } = 3;
    public int VolumeId { get; init; }
    public int TypeId { get; init; }
    public int Year { get; init; } = 2016;
    public int CopyrightId { get; init; } = 1;

    /// <summary>
    /// Frame visibility logic matching soundvoltex.dll at 0x4d88b9.
    /// When visible == 0 (Frame IDs 0, 3, 4, or character card illustrations),
    /// no frame, plaque, crest, or text overlay is drawn.
    /// </summary>
    public bool HasOverlayFrame
    {
        get
        {
            if (FrameId is 0 or 3 or 4) return false;
            if (TextureName.Contains("_ch_") && FrameId is not (1 or 7 or 11))
                return false;
            return true;
        }
    }

    /// <summary>
    /// Frame style matching soundvoltex.dll 16-entry table and rarity upgrade logic at 0x4d84f4.
    /// </summary>
    public FrameStyle Style
    {
        get
        {
            if (!HasOverlayFrame) return FrameStyle.None;

            // In soundvoltex.dll, entries 1, 2, 13 (visible == 2) upgrade to Gold if rarity >= 4
            if (FrameId is 1 or 2 or 13)
            {
                return Rarity >= 4 ? FrameStyle.Gold : FrameStyle.Silver;
            }

            return FrameId switch
            {
                5 => FrameStyle.Legend,
                6 or 7 => FrameStyle.Touhou,
                8 or 9 => FrameStyle.Silver,
                10 or 11 => FrameStyle.Gold,
                12 or 14 or 15 => FrameStyle.White,
                _ => Rarity >= 4 ? FrameStyle.Gold : FrameStyle.Silver
            };
        }
    }

    public FrameStyle FrameStyle => Style;

    /// <summary>
    /// Plaque style matching soundvoltex.dll field4 in 16-entry table.
    /// </summary>
    public PlaqueStyle PlaqueStyle
    {
        get
        {
            if (FrameId is 6) return PlaqueStyle.Silver;
            if (FrameId is 7 or 15) return PlaqueStyle.Touhou;
            if (FrameId is 1 or 8 or 14)
                return (FrameId == 1 && Rarity >= 4) ? PlaqueStyle.Gold : PlaqueStyle.Silver;
            return PlaqueStyle.Gold;
        }
    }

    public string SerialNumber
    {
        get
        {
            // Format matching soundvoltex.dll: SV%02d-%s%03d-%03d
            // e.g. SV03-cl001-010
            string prefix = "cl";
            if (TextureName.Contains("_ch_")) prefix = "ch";
            else if (TextureName.Contains("_lg_")) prefix = "lg";
            else if (TextureName.Contains("_sp_")) prefix = "sp";
            else if (TextureName.Contains("_toho_")) prefix = "to";

            int vol = VolumeId > 0 ? VolumeId : 1;
            int card = TypeId > 0 ? TypeId : 1;
            return $"SV{Version:D2}-{prefix}{vol:D3}-{card:D3}";
        }
    }
}

public sealed class GenesisCardCatalog
{
    private static readonly Lazy<GenesisCardCatalog> _lazy = new(() => new GenesisCardCatalog());
    public static GenesisCardCatalog Instance => _lazy.Value;

    private readonly Dictionary<string, GenesisCardInfo> _byTexture = new(StringComparer.OrdinalIgnoreCase);

    public GenesisCardCatalog()
    {
        LoadCatalog();
    }

    private void LoadCatalog()
    {
        try
        {
            Stream? stream = null;
            // Check filesystem first
            string[] searchPaths =
            {
                Path.Combine(AppContext.BaseDirectory, "Resources", "genesis_card.xml"),
                @"I:\KFC\contents\data\others\genesis_card.xml",
                @"data\others\genesis_card.xml",
                @"contents\data\others\genesis_card.xml"
            };

            foreach (var p in searchPaths)
            {
                if (File.Exists(p))
                {
                    stream = File.OpenRead(p);
                    break;
                }
            }

            if (stream == null)
            {
                var asm = typeof(GenesisCardCatalog).Assembly;
                stream = asm.GetManifestResourceStream("CPT50DTools.Resources.genesis_card.xml");
            }

            if (stream == null) return;

            using (stream)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                using var reader = new StreamReader(stream, Encoding.GetEncoding("shift-jis"));
                string text = reader.ReadToEnd();
                if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                {
                    int end = text.IndexOf("?>", StringComparison.Ordinal);
                    if (end >= 0) text = text[(end + 2)..];
                }

                var doc = XDocument.Parse("<root>" + text + "</root>");
                foreach (var el in doc.Descendants("card"))
                {
                    var infoEl = el.Element("info");
                    if (infoEl == null) continue;

                    string tex = infoEl.Element("texture")?.Value.Trim() ?? "";
                    if (string.IsNullOrEmpty(tex)) continue;

                    int.TryParse(el.Attribute("id")?.Value, out int id);
                    int.TryParse(infoEl.Element("version")?.Value, out int version);
                    int.TryParse(infoEl.Element("frame")?.Value, out int frame);
                    int.TryParse(infoEl.Element("rarity")?.Value, out int rarity);
                    int.TryParse(infoEl.Element("volume_id")?.Value, out int volId);
                    int.TryParse(infoEl.Element("type_id")?.Value, out int typeId);
                    int.TryParse(infoEl.Element("year")?.Value, out int year);
                    int.TryParse(infoEl.Element("copyright")?.Value, out int copyright);

                    string title = infoEl.Element("title")?.Value.Trim() ?? "";
                    string illust = infoEl.Element("illustrator")?.Value.Trim() ?? "";

                    var item = new GenesisCardInfo
                    {
                        Id = id,
                        TextureName = tex,
                        Title = title,
                        Illustrator = illust,
                        FrameId = frame,
                        Rarity = rarity > 0 ? rarity : 1,
                        Version = version > 0 ? version : 3,
                        VolumeId = volId,
                        TypeId = typeId,
                        Year = year > 0 ? year : 2016,
                        CopyrightId = copyright > 0 ? copyright : 1,
                    };

                    _byTexture[tex] = item;
                    // Also index without "gc_XX_" prefix if present
                    int first = tex.IndexOf('_');
                    if (first >= 0)
                    {
                        int second = tex.IndexOf('_', first + 1);
                        if (second >= 0) _byTexture[tex[(second + 1)..]] = item;
                    }
                }
            }
        }
        catch
        {
            // Silently allow fallback if catalog loading encounters issues
        }
    }

    public GenesisCardInfo Lookup(string rawName)
    {
        string clean = CleanKey(rawName);
        if (_byTexture.TryGetValue(clean, out var found)) return found;

        // Try matching against any texture ending with this key
        foreach (var (k, val) in _byTexture)
        {
            if (clean.EndsWith(k, StringComparison.OrdinalIgnoreCase) ||
                k.EndsWith(clean, StringComparison.OrdinalIgnoreCase))
            {
                return val;
            }
        }

        return CreateFallback(clean);
    }

    private static string CleanKey(string name)
    {
        string n = Path.GetFileNameWithoutExtension(name);
        if (n.EndsWith("_b", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_s", StringComparison.OrdinalIgnoreCase))
            n = n[..^2];
        return n;
    }

    private static GenesisCardInfo CreateFallback(string key)
    {
        int ver = 3;
        if (key.Contains("04_")) ver = 4;
        else if (key.Contains("05_")) ver = 5;
        else if (key.Contains("06_")) ver = 6;

        int frameId = 1;
        int rarity = 3;

        if (key.Contains("_ch_") || key.StartsWith("ch_"))
        {
            frameId = 3;
        }
        else if (key.Contains("_lg_") || key.StartsWith("lg_"))
        {
            frameId = 5;
            rarity = 5;
        }
        else if (key.Contains("toho") || key.Contains("_to_"))
        {
            frameId = 7;
        }

        return new GenesisCardInfo
        {
            TextureName = key,
            Title = key,
            Illustrator = "",
            FrameId = frameId,
            Rarity = rarity,
            Version = ver,
        };
    }
}
