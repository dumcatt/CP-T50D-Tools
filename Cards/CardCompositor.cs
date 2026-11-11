using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace CPT50DTools.Cards;

/// <summary>
/// Builds the printable card the way the game does: the 714x1074 artwork texture (which includes
/// print bleed) with the frame, bottom plaque or "ILLUST" tag, illustrator/title text, serial
/// number and copyright drawn on top.
///
/// All coordinates are in artwork texture pixels. They were measured against the game's own
/// pre-rendered thumbnails (*_s.ifs) and scans of printed cards.
/// </summary>
public static class CardCompositor
{
    public const int CardWidth = 714;
    public const int CardHeight = 1074;

    /// <summary>
    /// The visible card face (trim area) inside the 714x1074 print texture. Everything outside it
    /// is bleed. Note it is not centered in the texture.
    /// </summary>
    public static readonly RectangleF TrimRect = new(7f, 35.5f, 666f, 1003.6f);

    /// <summary>Trim rectangle scaled to an image of any size.</summary>
    public static RectangleF TrimRectFor(Size imageSize)
    {
        float sx = imageSize.Width / (float)CardWidth, sy = imageSize.Height / (float)CardHeight;
        return new RectangleF(TrimRect.X * sx, TrimRect.Y * sy, TrimRect.Width * sx, TrimRect.Height * sy);
    }

    /// <summary>
    /// Placement of the frame, plaque, ILLUST tag and text boxes, taken from the game's own card layouts
    /// (collection_frame / collection_frame2 AFP movies in data\graphics\verNN\card_data.ifs). Positions
    /// are element centres and half-sizes on the game's 712x1072 print stage, onto which the 714x1074
    /// artwork is drawn centred, so texture coordinates are stage coordinates + 1.
    /// </summary>
    private sealed record Layout(
        PointF Frame, PointF Plaque, PointF Tag,
        RectangleF Title, RectangleF ArtistOnPlaque, RectangleF ArtistOnTag, RectangleF Serial);

    private const float StageOffset = 1f;

    private static RectangleF Box(float cx, float cy, float hx, float hy) =>
        new(cx - hx + StageOffset, cy - hy + StageOffset, hx * 2, hy * 2);

    private static PointF At(float cx, float cy) => new(cx + StageOffset, cy + StageOffset);

    // ver03 (III)
    private static readonly Layout LayoutV3 = new(
        Frame: At(339, 537), Plaque: At(340, 947), Tag: At(183, 977),
        Title: Box(510, 987, 129, 10), ArtistOnPlaque: Box(168, 987, 129, 10), ArtistOnTag: Box(168, 987, 129, 10),
        Serial: Box(104, 1016, 75, 7));

    // ver04 / ver05 (IV+)
    private static readonly Layout LayoutV4 = new(
        Frame: At(340, 536), Plaque: At(340, 946), Tag: At(183, 984),
        Title: Box(162, 932, 130, 10), ArtistOnPlaque: Box(152, 980, 120, 10), ArtistOnTag: Box(158, 983, 120, 10),
        Serial: Box(111, 1011, 75, 7));

    // The game sets the title / illustrator with [sz:16] and the serial with [sz:12], in black.
    private const float TextSize = 16f, SerialSize = 12f;
    private static readonly Color TextColor = Color.Black;

    private static void DrawCentered(Graphics g, Bitmap bmp, PointF center) =>
        g.DrawImage(bmp, center.X - bmp.Width / 2f, center.Y - bmp.Height / 2f, bmp.Width, bmp.Height);

    /// <summary>Whether the "©year Konami Amusement" line is printed (user option).</summary>
    public static bool ShowCopyright { get; set; } = true;

    /// <summary>Whether the frame, plaque and ILLUST tag are drawn (user option). Frames baked into artwork stay.</summary>
    public static bool ShowFrame { get; set; } = true;

    /// <summary>Whether the illustrator, title and serial are printed (user option).</summary>
    public static bool ShowText { get; set; } = true;

    /// <summary>
    /// True when the printer's media reports the holo / overcoat flag (media type byte 2, bit 1).
    /// The game then brightens the artwork by an extra 3% (soundvoltex.dll, 0x4c55c0).
    /// </summary>
    public static bool HoloMediaBoost { get; set; }

    /// <summary>
    /// Brightness the game adds to the artwork's R, G and B: (bright + 3 on holo media) / 100,
    /// applied as a colour offset to the art layer only, not the frame or text.
    /// </summary>
    public static float ArtBrightness(GenesisCardInfo info) => (info.Bright + (HoloMediaBoost ? 3 : 0)) / 100f;

    private static void DrawArtwork(Graphics g, Bitmap artwork, float brightness, Rectangle dest)
    {
        if (brightness == 0)
        {
            g.DrawImage(artwork, dest);
            return;
        }
        // Additive offset on R, G, B (alpha untouched), like a Flash colour transform's add terms.
        var m = new ColorMatrix { Matrix40 = brightness, Matrix41 = brightness, Matrix42 = brightness };
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(m);
        g.DrawImage(artwork, dest, 0, 0, artwork.Width, artwork.Height, GraphicsUnit.Pixel, attrs);
    }

    public static Bitmap Compose(Bitmap artwork, string cardName, Bitmap? thumbnail = null)
    {
        var info = GenesisCardCatalog.Instance.Lookup(cardName);
        float brightness = ArtBrightness(info);
        bool anyOverlay = (info.HasOverlayFrame && ShowFrame) ||
                          ((info.HasOverlayFrame || info.HasArtOnlyText) && (ShowText || ShowCopyright));
        if (!anyOverlay)
        {
            if (brightness == 0) return (Bitmap)artwork.Clone();
            var art = new Bitmap(artwork.Width, artwork.Height, PixelFormat.Format32bppArgb);
            using var ga = Graphics.FromImage(art);
            ga.Clear(Color.Black);
            DrawArtwork(ga, artwork, brightness, new Rectangle(0, 0, artwork.Width, artwork.Height));
            return art;
        }

        var text = CardTextOverrides.Get(cardName, info);
        var lib = CardFrameLibrary.Instance;

        var result = new Bitmap(CardWidth, CardHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // The game clears its card render target to opaque black (D3D Clear 0xFF000000), so the
        // transparent margins around the artwork print black, not white.
        g.Clear(Color.Black);
        DrawArtwork(g, artwork, brightness, new Rectangle(0, 0, CardWidth, CardHeight));

        bool v4Layout = info.Version >= 4;
        var layout = v4Layout ? LayoutV4 : LayoutV3;

        lock (lib.SyncRoot)
        {
            var frame = info.FrameTexture != null ? lib.Get(info.Version, info.FrameTexture) : null;
            void DrawFrame()
            {
                if (frame != null && ShowFrame) DrawCentered(g, frame, layout.Frame);
            }

            // Layer order measured against the game's thumbnails: the plaque / ILLUST tag sits UNDER
            // the frame on every card except v3 gold-frame cards, where it is drawn on top.
            bool bottomOverFrame = !v4Layout && info.FrameTexture == "frame_gold";
            if (bottomOverFrame) DrawFrame();

            // IV+: TITLE box top-left, ILLUST box below it. III: ILLUST bottom-left, title bottom-right.
            if (info.PlaqueTexture != null && lib.Get(info.Version, info.PlaqueTexture) is { } plaque && ShowFrame)
                DrawCentered(g, plaque, layout.Plaque);
            if (info.ShowsIllustTag && lib.Get(info.Version, "illust_midashi") is { } tag && ShowFrame)
                DrawCentered(g, tag, layout.Tag);

            // Text goes in the layout's own boxes, even on cards drawn without a frame (frame id 3),
            // and is drawn after all artwork layers so nothing covers it.
            var lines = new List<(string text, RectangleF rect)>();
            if (info.ShowsTitle) lines.Add((text.Title, layout.Title));
            if (info.ShowsIllustrator)
                lines.Add((text.Illustrator, info.Bottom == CardBottom.IllustTag ? layout.ArtistOnTag : layout.ArtistOnPlaque));

            if (!bottomOverFrame) DrawFrame();

            var textColor = info.WhiteText ? Color.White : TextColor;
            if (ShowText)
                foreach (var (value, rect) in lines) DrawLine(g, value, rect, TextSize, textColor);
            if (ShowCopyright) DrawCopyright(g, lib, info, v4Layout);
        }

        if (ShowText) DrawSerial(g, text.Serial, layout.Serial);
        return result;
    }

    private static readonly Dictionary<(Bitmap, int), Rectangle> _bounds = new();

    /// <summary>Bounding box of a sprite's pixels with alpha above <paramref name="alpha"/> (cached).</summary>
    private static Rectangle VisibleBounds(Bitmap bmp, int alpha)
    {
        lock (_bounds)
        {
            if (_bounds.TryGetValue((bmp, alpha), out var r)) return r;
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (bmp.GetPixel(x, y).A > alpha)
                    {
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
            r = maxX < 0 ? new Rectangle(0, 0, bmp.Width, bmp.Height) : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            _bounds[(bmp, alpha)] = r;
            return r;
        }
    }

    private static void DrawLine(Graphics g, string value, RectangleF rect, float size, Color color)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        using var probe = CardFonts.Create(size);
        float width = g.MeasureString(value, probe).Width;
        if (width > rect.Width) size = Math.Max(9f, size * rect.Width / width);

        using var font = CardFonts.Create(size);
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        g.DrawString(value, font, brush, rect, sf);
    }

    private static void DrawSerial(Graphics g, string serial, RectangleF box)
    {
        if (string.IsNullOrWhiteSpace(serial)) return;
        using var font = CardFonts.Create(SerialSize);
        using var brush = new SolidBrush(TextColor);
        using var sf = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        // Long serials run past the 150 px box in the game too, so don't shrink or clip them.
        g.DrawString(serial, font, brush, new RectangleF(box.X, box.Y, 260, box.Height), sf);
    }

    /// <summary>Partner credits by bit of genesis_card.xml's copyright field (bit 1 is the Konami line).</summary>
    private static readonly (int Bit, string Texture)[] PartnerCredits =
    {
        (32, "c_konami_de"), // Hinabitter etc. (verified against scans)
        (16, "c_mixi"),      // Monster Strike volume
        (8, "c_exit"),       // only 2 cards use this bit; EXIT TUNES vs INTERNET is a best guess
        (4, "c_internet"),
        (2, "c_toho"),       // Touhou volumes (verified against scans)
    };

    /// <summary>
    /// Draws the copyright line. The catalog's copyright field is a bit mask: bit 1 prints
    /// "©year Konami Amusement" (built from the c, c_suji_N and c_konami_am sprites), and each other
    /// bit adds a partner credit to the left of it. III cards start the Konami line at x=365;
    /// IV+ cards right-align it to x=644.5.
    /// </summary>
    private static void DrawCopyright(Graphics g, CardFrameLibrary lib, GenesisCardInfo info, bool v4Layout)
    {
        const float scale = 0.95f;
        float cy = v4Layout ? 1026f : 1018.5f;
        int bits = info.CopyrightId;

        // Each piece: sprite, the part of it to draw, and the space before it (sprite px).
        var main = new List<(Bitmap Bmp, Rectangle Src, float Gap)>();
        if ((bits & 1) != 0 || bits == 0)
        {
            if (lib.Get(info.Version, "c") is { } c) main.Add((c, VisibleBounds(c, 24), 0));
            foreach (char d in info.Year.ToString())
                if (lib.Get(info.Version, $"c_suji_{d}") is { } digit)
                    main.Add((digit, VisibleBounds(digit, 128), main.Count == 1 ? 0.5f : 1f)); // digits: ink only, tight
            if (lib.Get(info.Version, "c_konami_am") is { } am) main.Add((am, VisibleBounds(am, 24), 1.5f));
        }
        var partners = PartnerCredits
            .Where(p => (bits & p.Bit) != 0)
            .Select(p => lib.Get(info.Version, p.Texture))
            .OfType<Bitmap>()
            .Select(b => (Bmp: b, Src: VisibleBounds(b, 24), Gap: 0f))
            .ToList();
        if (main.Count == 0 && partners.Count == 0) return;

        static float Width(List<(Bitmap Bmp, Rectangle Src, float Gap)> parts) => parts.Sum(p => (p.Src.Width + p.Gap) * scale);
        void DrawRun(List<(Bitmap Bmp, Rectangle Src, float Gap)> parts, float x)
        {
            foreach (var (bmp, src, gap) in parts)
            {
                x += gap * scale;
                g.DrawImage(bmp, new RectangleF(x, cy + (src.Y - bmp.Height / 2f) * scale, src.Width * scale, src.Height * scale), src, GraphicsUnit.Pixel);
                x += src.Width * scale;
            }
        }

        float mainX = v4Layout ? 644.5f - Width(main) : 365f;
        if (main.Count > 0) DrawRun(main, mainX);

        // Partner credits go to the left of the Konami line, right to left.
        float partnerGap = v4Layout ? 17f : 11f;
        float right = main.Count > 0 ? mainX - partnerGap : (v4Layout ? 644.5f : 365f + partners.Sum(p => p.Src.Width * scale) + partnerGap);
        foreach (var p in partners)
        {
            float w = p.Src.Width * scale;
            DrawRun(new() { p }, right - w);
            right -= w + partnerGap;
        }
    }
}
