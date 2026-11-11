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
    /// <summary>genesis_card.xml "bright": percent added to the artwork's R, G and B when the game renders it.</summary>
    public int Bright { get; init; }

    /// <summary>
    /// The game's frame table (soundvoltex.dll, built at 0x4d8244 and looked up by frame id):
    /// which AFP layout is used, the frame texture, how the frame is chosen, and whether the
    /// title / illustrator are printed. Mode 0 hides the frame, plaque and ILLUST tag; mode 1 uses
    /// the texture as-is; mode 2 upgrades silver to gold for rarity 4 and up.
    /// </summary>
    private static readonly (CardBottom layout, int texture, int mode, bool title, bool illustrator)[] FrameTable =
    {
        (CardBottom.Plaque, 0, 0, false, false),    // 0
        (CardBottom.Plaque, 0, 2, true, true),      // 1
        (CardBottom.IllustTag, 0, 2, false, true),  // 2
        (CardBottom.Plaque, 0, 0, false, true),     // 3: no frame, illustrator only
        (CardBottom.Plaque, 0, 0, false, false),    // 4
        (CardBottom.Plaque, 2, 1, true, true),      // 5: lg01, white text
        (CardBottom.Plaque, 3, 1, true, true),      // 6
        (CardBottom.IllustTag, 3, 1, false, true),  // 7
        (CardBottom.Plaque, 0, 1, true, true),      // 8
        (CardBottom.IllustTag, 0, 1, false, true),  // 9
        (CardBottom.Plaque, 1, 1, true, true),      // 10
        (CardBottom.IllustTag, 1, 1, false, true),  // 11
        (CardBottom.IllustTag, 4, 1, false, false), // 12: white2 (character profile)
        (CardBottom.IllustTag, 0, 2, false, false), // 13: silver / gold frame, no tag or text
        (CardBottom.Plaque, 4, 1, false, false),    // 14
        (CardBottom.None, 4, 1, false, false),      // 15: art-only layout, no frame shown
    };

    // Frame textures by table index, and the plaque ("title_name") used with each; lg01, white2 and
    // creator have none.
    private static readonly string[] FrameTextures = { "frame_sil", "frame_gold", "frame_lg01", "frame_toho01", "frame_white2", "frame_creator" };
    private static readonly string?[] PlaqueTextures = { "frame_sil_name", "frame_gold_name", null, "frame_toho_name", null, null };

    private (CardBottom layout, int texture, int mode, bool title, bool illustrator) Entry =>
        FrameId >= 0 && FrameId < FrameTable.Length ? FrameTable[FrameId] : FrameTable[0];

    private int TextureIndex
    {
        get
        {
            var e = Entry;
            return e.mode == 2 && Rarity >= 4 ? Math.Min(e.texture + 1, FrameTextures.Length - 1) : e.texture;
        }
    }

    /// <summary>False when no frame is drawn over the artwork.</summary>
    public bool HasOverlayFrame => FrameTexture != null;

    /// <summary>
    /// card_data.ifs texture name of the frame, or null. The art-only layout (collection_frame3, frame
    /// id 15) has no "frame" placeholder, so the texture the table names is never shown there.
    /// </summary>
    public string? FrameTexture => Entry.mode == 0 || Entry.layout == CardBottom.None ? null : FrameTextures[TextureIndex];

    /// <summary>
    /// card_data.ifs texture name of the bottom plaque, or null. The game only shows it on the
    /// plaque layout, with a frame, when the illustrator is printed.
    /// </summary>
    public string? PlaqueTexture =>
        Entry is { layout: CardBottom.Plaque, illustrator: true, mode: not 0 } ? PlaqueTextures[TextureIndex] : null;

    /// <summary>Which layout the card uses (plaque, ILLUST tag, or artwork only).</summary>
    public CardBottom Bottom => Entry.layout;

    /// <summary>The ILLUST tag is drawn on tag-layout cards with a frame that print the illustrator.</summary>
    public bool ShowsIllustTag => Entry is { layout: CardBottom.IllustTag, illustrator: true, mode: not 0 };

    /// <summary>Whether the game prints the title / illustrator for this frame id.</summary>
    public bool ShowsTitle => FromCatalog && Entry.title;
    public bool ShowsIllustrator => FromCatalog && Entry.illustrator;

    /// <summary>The game prints the title / illustrator in white on the lg01 frame, black otherwise.</summary>
    public bool WhiteText => FrameTexture == "frame_lg01";

    /// <summary>True for cards defined in genesis_card.xml (not fallback info for loose images).</summary>
    public bool FromCatalog => VolumeTotal > 0;

    /// <summary>Every catalog card gets the serial and copyright printed, framed or not.</summary>
    public bool HasArtOnlyText => !HasOverlayFrame && FromCatalog;

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
            int.TryParse(infoEl.Element("bright")?.Value, out int bright);

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
                Bright = bright,
            };

            _byTexture[tex] = item;
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

        // No partial / suffix matching: textures missing from genesis_card.xml (unused art in the
        // game files) used to borrow another card's frame, text and serial that way.

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
            Title = "", // not in genesis_card.xml: left blank, editable in Card text
            Illustrator = "",
            FrameId = frameId,
            Rarity = rarity,
            Version = ver,
        };
    }
}
