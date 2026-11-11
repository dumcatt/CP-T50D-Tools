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

    // Frame and plaque are centered on the card face; frame center measured at (342, 539).
    private const float FrameCenterX = 342f, FrameCenterY = 539f;
    private const float PlaqueCenterX = 340f, PlaqueY = 873f;
    private const float TagX = 24f, TagYv3 = 936f, TagYv4 = 948f;

    /// <summary>Whether the "©year Konami Amusement" line is printed (user option).</summary>
    public static bool ShowCopyright { get; set; } = true;

    /// <summary>Whether the frame, plaque and ILLUST tag are drawn (user option). Frames baked into artwork stay.</summary>
    public static bool ShowFrame { get; set; } = true;

    /// <summary>Whether the illustrator, title and serial are printed (user option).</summary>
    public static bool ShowText { get; set; } = true;

    public static Bitmap Compose(Bitmap artwork, string cardName, Bitmap? thumbnail = null)
    {
        var info = GenesisCardCatalog.Instance.Lookup(cardName);
        bool anyOverlay = (info.HasOverlayFrame && ShowFrame) ||
                          ((info.HasOverlayFrame || info.HasArtOnlyText) && (ShowText || ShowCopyright));
        if (!anyOverlay)
            return (Bitmap)artwork.Clone();

        var text = CardTextOverrides.Get(cardName, info);
        var lib = CardFrameLibrary.Instance;

        var result = new Bitmap(CardWidth, CardHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        g.Clear(Color.White);
        g.DrawImage(artwork, 0, 0, CardWidth, CardHeight);

        bool v4Layout = info.Version >= 4;

        lock (lib.SyncRoot)
        {
            var frame = info.FrameTexture != null ? lib.Get(info.Version, info.FrameTexture) : null;
            void DrawFrame()
            {
                if (frame != null && ShowFrame)
                    g.DrawImage(frame, FrameCenterX - frame.Width / 2f, FrameCenterY - frame.Height / 2f, frame.Width, frame.Height);
            }

            // Layer order measured against the game's thumbnails: the plaque / ILLUST tag sits UNDER
            // the frame on every card except v3 gold-frame cards, where it is drawn on top.
            bool bottomOverFrame = !v4Layout && info.FrameTexture == "frame_gold";
            if (bottomOverFrame) DrawFrame();

            // Text is collected and drawn after all artwork layers so nothing covers it.
            var lines = new List<(string text, RectangleF rect)>();
            if (info.Bottom == CardBottom.Plaque && info.PlaqueTexture != null &&
                lib.Get(info.Version, info.PlaqueTexture) is { } plaque)
            {
                float px = PlaqueCenterX - plaque.Width / 2f;
                if (ShowFrame) g.DrawImage(plaque, px, PlaqueY, plaque.Width, plaque.Height);
                if (v4Layout)
                {
                    // IV+: TITLE box top-left, ILLUST box below it, logo on the right.
                    lines.Add((text.Title, new RectangleF(px + 4, PlaqueY + 46, 276, 26)));
                    lines.Add((text.Illustrator, new RectangleF(px + 5, PlaqueY + 94, 275, 26)));
                }
                else
                {
                    // III: ILLUST bottom-left, title left-aligned under the logo.
                    lines.Add((text.Illustrator, new RectangleF(px + 10, PlaqueY + 102, 240, 26)));
                    lines.Add((text.Title, new RectangleF(px + 348, PlaqueY + 102, 262, 26)));
                }
            }
            else if (info.Bottom == CardBottom.IllustTag && lib.Get(info.Version, "illust_midashi") is { } tag)
            {
                float ty = v4Layout ? TagYv4 : TagYv3;
                if (ShowFrame) g.DrawImage(tag, TagX, ty, tag.Width, tag.Height);
                lines.Add((text.Illustrator, new RectangleF(TagX + (v4Layout ? 12 : 11), ty + (v4Layout ? 22 : 38), 230, 26)));
            }
            else if (info.PrintsIllustratorOnArt)
            {
                // Artwork-only cards have the ILLUST label baked in at the tag position; the name is printed under it.
                float ty = v4Layout ? TagYv4 : TagYv3;
                lines.Add((text.Illustrator, new RectangleF(TagX + (v4Layout ? 5 : 11), ty + (v4Layout ? 18.5f : 38), 230, 26)));
            }

            if (!bottomOverFrame) DrawFrame();

            // IV+ cards print the title / illustrator at ~16px; III cards at ~18px.
            float textSize = v4Layout ? 16f : 18f;
            if (ShowText)
                foreach (var (value, rect) in lines) DrawLine(g, value, rect, textSize);
            if (ShowCopyright) DrawCopyright(g, lib, info, v4Layout);
        }

        if (ShowText) DrawSerial(g, text.Serial, v4Layout);
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

    private static void DrawLine(Graphics g, string value, RectangleF rect, float size)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        using var probe = CardFonts.Create(size);
        float width = g.MeasureString(value, probe).Width;
        if (width > rect.Width) size = Math.Max(9f, size * rect.Width / width);

        using var font = CardFonts.Create(size);
        using var brush = new SolidBrush(Color.FromArgb(30, 30, 30));
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        g.DrawString(value, font, brush, rect, sf);
    }

    private static void DrawSerial(Graphics g, string serial, bool v4Layout)
    {
        if (string.IsNullOrWhiteSpace(serial)) return;
        using var font = CardFonts.Create(12.3f);
        using var brush = new SolidBrush(Color.FromArgb(25, 25, 25));
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        float x = v4Layout ? 34f : 27f, cy = v4Layout ? 1012f : 1016.5f;
        g.DrawString(serial, font, brush, new RectangleF(x, cy - 10, 260, 20), sf);
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
