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
                    lines.Add((text.Title, new RectangleF(px + 10, PlaqueY + 47, 270, 26)));
                    lines.Add((text.Illustrator, new RectangleF(px + 10, PlaqueY + 94, 270, 26)));
                }
                else
                {
                    // III: ILLUST bottom-left, title left-aligned under the logo.
                    lines.Add((text.Illustrator, new RectangleF(px + 22, PlaqueY + 102, 230, 26)));
                    lines.Add((text.Title, new RectangleF(px + 360, PlaqueY + 102, 250, 26)));
                }
            }
            else if (info.Bottom == CardBottom.IllustTag && lib.Get(info.Version, "illust_midashi") is { } tag)
            {
                float ty = v4Layout ? TagYv4 : TagYv3;
                if (ShowFrame) g.DrawImage(tag, TagX, ty, tag.Width, tag.Height);
                lines.Add((text.Illustrator, new RectangleF(TagX + (v4Layout ? 11 : 22), ty + (v4Layout ? 15 : 31), 220, 26)));
            }
            else if (info.PrintsIllustratorOnArt)
            {
                // Artwork-only cards have the ILLUST label baked in at the tag position; the name is printed under it.
                float ty = v4Layout ? TagYv4 : TagYv3;
                lines.Add((text.Illustrator, new RectangleF(TagX + (v4Layout ? 11 : 22), ty + (v4Layout ? 15 : 31), 220, 26)));
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

    private static readonly Dictionary<Bitmap, Rectangle> _bounds = new();

    /// <summary>Bounding box of the non-transparent pixels of a sprite (cached).</summary>
    private static Rectangle VisibleBounds(Bitmap bmp)
    {
        lock (_bounds)
        {
            if (_bounds.TryGetValue(bmp, out var r)) return r;
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (bmp.GetPixel(x, y).A > 24)
                    {
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
            r = maxX < 0 ? new Rectangle(0, 0, bmp.Width, bmp.Height) : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            _bounds[bmp] = r;
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
        using var font = CardFonts.Create(13.5f);
        using var brush = new SolidBrush(Color.FromArgb(25, 25, 25));
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        float x = v4Layout ? 40f : 36f, cy = v4Layout ? 1011f : 1015f;
        g.DrawString(serial, font, brush, new RectangleF(x, cy - 10, 260, 20), sf);
    }

    /// <summary>"©" + year digits + "Konami Amusement", or the partner's complete "©Company" sprite.</summary>
    private static void DrawCopyright(Graphics g, CardFrameLibrary lib, GenesisCardInfo info, bool v4Layout)
    {
        var parts = new List<Bitmap>();
        var digits = new HashSet<Bitmap>();
        if (info.CopyrightId is 2 or 3 or 4 or 5)
        {
            if (lib.Get(info.Version, CardFrameLibrary.CopyrightTexture(info.CopyrightId)!) is { } partner) parts.Add(partner);
        }
        else if (info.Year >= 2016)
        {
            if (lib.Get(info.Version, "c") is { } c) parts.Add(c);
            foreach (char d in info.Year.ToString())
                if (lib.Get(info.Version, $"c_suji_{d}") is { } digit) { parts.Add(digit); digits.Add(digit); }
            if (lib.Get(info.Version, "c_konami_am") is { } am) parts.Add(am);
        }
        else if (lib.Get(info.Version, "c_konami_de") is { } de)
        {
            parts.Add(de);
        }
        if (parts.Count == 0) return;

        // Sprites are drawn trimmed to their visible pixels and packed tightly (III: 0.86 scale, IV+: 0.95).
        float scale = v4Layout ? 0.95f : 0.86f;
        // The c_suji digit sprites are a fixed-pitch font: each glyph's ink sits at x=2..9 of its
        // 12px cell. Draw digits as whole cells, starting 2px left of the pen, at an 8px pitch.
        const float digitPitch = 8f, digitInkLeft = 2f;
        var boxes = parts.Select(p => digits.Contains(p) ? new Rectangle(0, 0, p.Width, p.Height) : VisibleBounds(p)).ToList();
        float Cell(int i) => digits.Contains(parts[i]) ? digitPitch : boxes[i].Width;
        float total = 0;
        for (int i = 0; i < parts.Count; i++) total += (Cell(i) + Gap(i)) * scale;
        float x = v4Layout ? 637f - total : 365f;
        float cy = v4Layout ? 1025f : 1017f;
        for (int i = 0; i < parts.Count; i++)
        {
            var src = boxes[i];
            x += Gap(i) * scale;
            float dx = digits.Contains(parts[i]) ? -digitInkLeft * scale : 0;
            g.DrawImage(parts[i], new RectangleF(x + dx, cy + (src.Y - parts[i].Height / 2f) * scale, src.Width * scale, src.Height * scale), src, GraphicsUnit.Pixel);
            x += Cell(i) * scale;
        }

        // 1px between characters, a little more before the company name.
        float Gap(int i) => i == 0 || digits.Contains(parts[i]) ? 0 : (i == parts.Count - 1 && parts.Count > 1 ? (v4Layout ? 1 : 3) : (v4Layout ? 0 : 1));
    }
}
