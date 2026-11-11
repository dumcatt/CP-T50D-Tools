using System.Text;
using System.Xml.Linq;

namespace CPT50DTools.Cards;

/// <summary>What is drawn along the bottom of a framed card.</summary>
public enum CardBottom
{
    None,
    /// <summary>Full-width logo plaque (frame_*_name) holding the title and illustrator.</summary>
    Plaque,
    /// <summary>Small "ILLUST" tag (illust_midashi) with the illustrator name.</summary>
    IllustTag,
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
    /// Layout per frame id, verified against the game's own pre-rendered *_s.ifs thumbnails
    /// (frame ids 1, 2, 3, 7, 8, 10, 11, 12, 15). Ids 5, 6, 9, 13 and 14 do not occur in the
    /// shipped card data and use a best guess.
    /// </summary>
    private (string? frame, string? plaque, CardBottom bottom) Layout
    {
        get
        {
            if (TextureName.Contains("_ch_") && FrameId is not (1 or 2 or 7 or 8 or 10 or 11))
                return (null, null, CardBottom.None);

            bool gold = Rarity >= 4;
            return FrameId switch
            {
                1 => gold ? ("frame_gold", "frame_gold_name", CardBottom.Plaque) : ("frame_sil", "frame_sil_name", CardBottom.Plaque),
                2 or 13 => (gold ? "frame_gold" : "frame_sil", null, CardBottom.IllustTag),
                5 => ("frame_lg01", "frame_gold_name", CardBottom.Plaque),
                6 => ("frame_toho01", "frame_toho_name", CardBottom.Plaque),
                7 => ("frame_toho01", null, CardBottom.IllustTag),
                8 => ("frame_sil", "frame_sil_name", CardBottom.Plaque),
                9 => ("frame_sil", null, CardBottom.IllustTag),
                10 => ("frame_gold", "frame_gold_name", CardBottom.Plaque),
                11 => ("frame_gold", null, CardBottom.IllustTag),
                14 => ("frame_white2", "frame_sil_name", CardBottom.Plaque),
                // 0, 3, 4, 12, 15: the artwork already is the finished card.
                _ => (null, null, CardBottom.None),
            };
        }
    }

    /// <summary>False when the artwork is used as-is (no frame, plaque or text is drawn).</summary>
    public bool HasOverlayFrame => Layout.frame != null;

    /// <summary>card_data.ifs texture name of the frame, or null.</summary>
    public string? FrameTexture => Layout.frame;

    /// <summary>card_data.ifs texture name of the bottom plaque, or null.</summary>
    public string? PlaqueTexture => Layout.plaque;

    public CardBottom Bottom => Layout.bottom;

    /// <summary>True for cards defined in genesis_card.xml (not fallback info for loose images).</summary>
    public bool FromCatalog => VolumeTotal > 0;

    /// <summary>
    /// Artwork-only cards (frame 0, 3, 4, 12, 15) still get the serial and copyright printed
    /// on top; verified against the game's thumbnails and printed-card scans.
    /// </summary>
    public bool HasArtOnlyText => !HasOverlayFrame && FromCatalog;

    /// <summary>
    /// Artwork-only cards whose art carries an ILLUST label (frame 0, 3, 4) also get the
    /// illustrator name printed under it. Frame 12 (character profile) and 15 have it in the art.
    /// </summary>
    public bool PrintsIllustratorOnArt => HasArtOnlyText && FrameId is not (12 or 15);

    public int VolumeType { get; init; }
    public int Volume { get; init; }
    /// <summary>Number of cards in this card's volume (the "/040" part of the serial).</summary>
    public int VolumeTotal { get; internal set; }

    /// <summary>
    /// Printed serial, e.g. SV03-N001-002/040: version, volume type letter (0 = N regular,
    /// 1 = S special, 2 = E), volume, number in volume and volume size. Verified against scans.
    /// </summary>
    public string SerialNumber
    {
        get
        {
            if (Volume <= 0 || VolumeId <= 0) return "";
            string letter = VolumeType switch { 0 => "N", 1 => "S", 2 => "E", _ => "S" };
            string total = VolumeTotal > 0 ? $"/{VolumeTotal:D3}" : "";
            return $"SV{Version:D2}-{letter}{Volume:D3}-{VolumeId:D3}{total}";
        }
    }

}

public sealed class GenesisCardCatalog
{
    private static volatile GenesisCardCatalog _instance = new();

    /// <summary>The catalog for the currently loaded game data folder (empty until one is loaded).</summary>
    public static GenesisCardCatalog Instance => _instance;

    /// <summary>Path of the genesis_card.xml the current catalog was read from, or null.</summary>
    public string? SourcePath { get; private set; }
    public int Count => _byTexture.Count;

    private readonly Dictionary<string, GenesisCardInfo> _byTexture = new(StringComparer.OrdinalIgnoreCase);

    private GenesisCardCatalog() { }

    /// <summary>Loads the game's genesis_card.xml and makes it the active catalog.</summary>
    public static GenesisCardCatalog LoadFrom(string xmlPath)
    {
        var catalog = new GenesisCardCatalog { SourcePath = xmlPath };
        using (var stream = File.OpenRead(xmlPath))
            catalog.Parse(stream);
        _instance = catalog;
        return catalog;
    }

    /// <summary>Drops the active catalog (cards then use fallback info derived from their names).</summary>
    public static void Reset() => _instance = new GenesisCardCatalog();

    private void Parse(Stream stream)
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
            int.TryParse(infoEl.Element("volume_type")?.Value, out int volType);
            int.TryParse(infoEl.Element("volume")?.Value, out int volume);
            int.TryParse(infoEl.Element("type_id")?.Value, out int typeId);
            int.TryParse(infoEl.Element("year")?.Value, out int year);
            int.TryParse(infoEl.Element("copyright")?.Value, out int copyright);

            var item = new GenesisCardInfo
            {
                Id = id,
                TextureName = tex,
                Title = infoEl.Element("title")?.Value.Trim() ?? "",
                Illustrator = infoEl.Element("illustrator")?.Value.Trim() ?? "",
                FrameId = frame,
                Rarity = rarity > 0 ? rarity : 1,
                Version = version > 0 ? version : 3,
                VolumeId = volId,
                VolumeType = volType,
                Volume = volume,
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

        foreach (var group in _byTexture.Values.Distinct().GroupBy(c => (c.Version, c.VolumeType, c.Volume)))
        {
            int total = group.Count();
            foreach (var c in group) c.VolumeTotal = total;
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
