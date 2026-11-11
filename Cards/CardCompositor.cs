using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace CPT50DTools.Cards;

/// <summary>
/// Composites high-resolution character artwork with native vector-grade card frames,
/// plaques, version crests, and authentic typography, matching soundvoltex.dll's print engine.
/// </summary>
public static class CardCompositor
{
    private static readonly string[] JapaneseFontCandidates = { "Meiryo", "Yu Gothic", "MS Gothic", "Segoe UI" };
    private static string? _chosenFontFamily;

    private static string GetFontFamily()
    {
        if (_chosenFontFamily != null) return _chosenFontFamily;
        var installed = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in JapaneseFontCandidates)
        {
            if (installed.Contains(candidate))
            {
                _chosenFontFamily = candidate;
                return candidate;
            }
        }
        _chosenFontFamily = FontFamily.GenericSansSerif.Name;
        return _chosenFontFamily;
    }

    public static Bitmap Compose(Bitmap artwork, string cardName, Bitmap? thumbnail = null)
    {
        var info = GenesisCardCatalog.Instance.Lookup(cardName);

        // If card has no overlay frame (Frame IDs 0, 3, 4, character PUR cards, etc.)
        // return the authentic raw artwork directly without adding extra frames or plaques.
        if (!info.HasOverlayFrame)
        {
            return (Bitmap)artwork.Clone();
        }

        int w = artwork.Width;
        int h = artwork.Height;

        var lib = CardFrameLibrary.Instance;
        var frame = lib.GetFrame(info.FrameStyle);
        var plaque = lib.GetPlaque(info.PlaqueStyle);
        var midashi = lib.GetMidashi(info.Version);
        var copyright = lib.GetCopyright(info.CopyrightId);

        if (frame == null || plaque == null)
        {
            return (Bitmap)artwork.Clone();
        }

        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Base card stock and artwork
        g.Clear(Color.White);
        g.DrawImage(artwork, 0, 0, w, h);

        // Scale factors: Reference dimensions are 714 x 1074
        float sx = (float)w / 714f;
        float sy = (float)h / 1074f;

        // 1. High-resolution metallic frame centered to fit artwork (28, 38)
        int frameW = (int)Math.Round(frame.Width * sx);
        int frameH = (int)Math.Round(frame.Height * sy);
        int frameX = (w - frameW) / 2;
        int frameY = (h - frameH) / 2;
        g.DrawImage(frame, frameX, frameY, frameW, frameH);

        // 2. Banner plaque centered at the bottom of the frame
        int plaqueW = (int)Math.Round(plaque.Width * sx);
        int plaqueH = (int)Math.Round(plaque.Height * sy);
        int plaqueX = (w - plaqueW) / 2;
        int plaqueY = frameY + frameH - plaqueH - (int)Math.Round(12 * sy);
        g.DrawImage(plaque, plaqueX, plaqueY, plaqueW, plaqueH);

        // 3. Version crest centered on the plaque
        if (midashi != null)
        {
            int midW = (int)Math.Round(midashi.Width * sx);
            int midH = (int)Math.Round(midashi.Height * sy);
            int midX = (w - midW) / 2;
            int midY = plaqueY + (int)Math.Round(29 * sy);
            g.DrawImage(midashi, midX, midY, midW, midH);
        }

        // 4. Copyright logo at bottom-right
        if (copyright != null)
        {
            int cW = (int)Math.Round(copyright.Width * sx);
            int cH = (int)Math.Round(copyright.Height * sy);
            int cX = plaqueX + plaqueW - cW - (int)Math.Round(10 * sx);
            int cY = plaqueY + plaqueH - cH - (int)Math.Round(14 * sy);
            g.DrawImage(copyright, cX, cY, cW, cH);
        }

        // 5. Typography (Illustrator, Title, Serial Number)
        RenderTypography(g, info, plaqueX, plaqueY, plaqueW, plaqueH, sx, sy, w, h);

        return result;
    }

    private static void RenderTypography(Graphics g, GenesisCardInfo info,
        int plaqueX, int plaqueY, int plaqueW, int plaqueH,
        float sx, float sy, int w, int h)
    {
        string fontName = GetFontFamily();

        using var fontSerial = new Font("Segoe UI", 9f * sy, FontStyle.Bold);

        Color textColor = info.FrameStyle switch
        {
            FrameStyle.Silver => Color.FromArgb(40, 40, 40),
            _ => Color.FromArgb(45, 38, 25)
        };

        using var textBrush = new SolidBrush(textColor);
        using var serialBrush = new SolidBrush(Color.FromArgb(30, 30, 30));

        // Serial number (bottom-left)
        string serial = info.SerialNumber;
        float serialX = plaqueX + 16 * sx;
        float serialY = plaqueY + plaqueH - 22 * sy;
        g.DrawString(serial, fontSerial, serialBrush, serialX, serialY);

        // Left plaque: Illustrator Name
        if (!string.IsNullOrWhiteSpace(info.Illustrator))
        {
            float illustLeft = plaqueX + 22 * sx;
            float illustWidth = 145 * sx;
            float textY = plaqueY + 54 * sy;
            float textH = 34 * sy;

            float fontSize = 12f * sy;
            using var fontIllust = new Font(fontName, fontSize, FontStyle.Bold);
            var sz = g.MeasureString(info.Illustrator, fontIllust);
            if (sz.Width > illustWidth)
            {
                fontSize = Math.Max(7.5f * sy, fontSize * (illustWidth / sz.Width));
            }

            using var fontIllustScaled = new Font(fontName, fontSize, FontStyle.Bold);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            var rect = new RectangleF(illustLeft, textY, illustWidth, textH);
            g.DrawString(info.Illustrator, fontIllustScaled, textBrush, rect, sf);
        }

        // Right plaque: Title Name
        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            float titleLeft = plaqueX + plaqueW - 167 * sx;
            float titleWidth = 145 * sx;
            float textY = plaqueY + 54 * sy;
            float textH = 34 * sy;

            float fontSize = 12f * sy;
            using var fontTitle = new Font(fontName, fontSize, FontStyle.Bold);
            var sz = g.MeasureString(info.Title, fontTitle);
            if (sz.Width > titleWidth)
            {
                fontSize = Math.Max(7.5f * sy, fontSize * (titleWidth / sz.Width));
            }

            using var fontTitleScaled = new Font(fontName, fontSize, FontStyle.Bold);
            using var sfCenter = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            var titleRect = new RectangleF(titleLeft, textY, titleWidth, textH);
            g.DrawString(info.Title, fontTitleScaled, textBrush, titleRect, sfCenter);
        }
    }
}
