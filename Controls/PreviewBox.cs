using System.Drawing.Drawing2D;
using CPT50DTools.Cards;

namespace CPT50DTools.Controls;

/// <summary>
/// Gray preview surface (AssetStudio style) that renders the card image faithfully
/// with high-quality bicubic filtering, matching thumbnail rendering, and overlays
/// the KMAT overcoat watermark pattern when selected.
/// </summary>
public sealed class PreviewBox : Control
{
    private Image? _image;
    private string _placeholder = "Preview\nnot\navailable";
    private int _overcoatIndex;
    private bool _showBleed = true;

    public PreviewBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.FromArgb(160, 160, 160);
    }

    public Image? Image
    {
        get => _image;
        set { _image = value; Invalidate(); }
    }

    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; Invalidate(); }
    }

    public int OvercoatIndex
    {
        get => _overcoatIndex;
        set { _overcoatIndex = value; Invalidate(); }
    }

    /// <summary>
    /// True: show the whole print area with the bleed shaded and the trim line marked.
    /// False: show only the card face (what is left after trimming).
    /// </summary>
    public bool ShowBleed
    {
        get => _showBleed;
        set { _showBleed = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_image == null)
        {
            float size = Math.Clamp(Math.Min(Width, Height) / 9f, 10f, 40f);
            using var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var b = new SolidBrush(Color.FromArgb(90, 90, 90));
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(_placeholder, f, b, ClientRectangle, sf);
            return;
        }

        const int margin = 8;
        var available = Rectangle.Inflate(ClientRectangle, -margin, -margin);
        if (available.Width <= 0 || available.Height <= 0) return;

        var trim = CardCompositor.TrimRectFor(_image.Size);
        var src = _showBleed ? new RectangleF(0, 0, _image.Width, _image.Height) : trim;

        double s = Math.Min(available.Width / (double)src.Width, available.Height / (double)src.Height);
        int w = Math.Max(1, (int)Math.Round(src.Width * s));
        int h = Math.Max(1, (int)Math.Round(src.Height * s));
        var dst = new Rectangle(available.X + (available.Width - w) / 2, available.Y + (available.Height - h) / 2, w, h);

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        g.DrawImage(_image, dst, src, GraphicsUnit.Pixel);

        if (_showBleed)
        {
            // Shade the bleed and mark the trim line.
            var trimDst = new RectangleF(
                dst.X + (float)(trim.X * s), dst.Y + (float)(trim.Y * s),
                (float)(trim.Width * s), (float)(trim.Height * s));
            using (var region = new Region(dst))
            {
                region.Exclude(trimDst);
                using var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
                g.FillRegion(shade, region);
            }
            using var trimPen = new Pen(Color.FromArgb(230, 0, 200, 255), 1f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(trimPen, trimDst.X, trimDst.Y, trimDst.Width, trimDst.Height);
        }

        // Subtle 1px border around the card edge (matching thumbnail border)
        using (var p = new Pen(Color.FromArgb(120, 120, 120)))
        {
            g.DrawRectangle(p, dst.X, dst.Y, dst.Width - 1, dst.Height - 1);
        }

        // Overlay KMAT watermark pattern if active
        if (_overcoatIndex > 0)
        {
            bool isPortrait = _image.Height >= _image.Width;
            OvercoatManager.Instance.RenderOvercoat(g, dst, _overcoatIndex, isPortrait);

            // Small badge in top-right corner
            string badgeText = $"★ KMAT 0{_overcoatIndex}";
            using var badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            var sz = g.MeasureString(badgeText, badgeFont);
            var badgeRect = new Rectangle(dst.Right - (int)sz.Width - 10, dst.Top + 6, (int)sz.Width + 6, (int)sz.Height + 2);
            using (var bBg = new SolidBrush(Color.FromArgb(180, 20, 20, 20)))
                g.FillRectangle(bBg, badgeRect);
            using (var bFg = new SolidBrush(Color.Gold))
                g.DrawString(badgeText, badgeFont, bFg, badgeRect.X + 3, badgeRect.Y + 1);
        }
    }
}
