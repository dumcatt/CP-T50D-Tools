using System.Drawing.Drawing2D;
using CPT50DTools.Cards;

namespace CPT50DTools.Controls;

/// <summary>
/// Gray preview surface (AssetStudio style) that renders the card image faithfully
/// with high-quality bicubic filtering, matching thumbnail rendering, and overlays
/// the KMAT overcoat watermark pattern when selected.
/// Mouse wheel zooms around the cursor, left-drag pans, double-click resets to fit.
/// </summary>
public sealed class PreviewBox : Control
{
    private const float MinZoom = 1f, MaxZoom = 16f, WheelStep = 1.25f;
    // Pixels of the card that must stay on screen while panning.
    private const float KeepVisible = 40f;

    private Image? _image;
    private string _placeholder = "Preview\nnot\navailable";
    private int _overcoatIndex;
    private bool _showBleed = true;

    // 1 = fit to the control; _pan offsets the card from its centred position (client pixels).
    private float _zoom = 1f;
    private PointF _pan;
    private Point? _dragStart;
    private PointF _panAtDragStart;

    /// <summary>Raised when the zoom or pan changes (including a reset).</summary>
    public event EventHandler? ViewChanged;

    public PreviewBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(160, 160, 160);
    }

    /// <summary>True when the preview is zoomed or panned away from the default fit.</summary>
    public bool IsViewChanged => _zoom != 1f || _pan != PointF.Empty;

    /// <summary>Back to the whole card fitted in the control.</summary>
    public void ResetView()
    {
        if (!IsViewChanged) return;
        _zoom = 1f;
        _pan = PointF.Empty;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
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
        PaintCard(e.Graphics);
        DrawResetButton(e.Graphics);
    }

    private bool _resetHover;

    /// <summary>"⟲ Reset view" overlay in the bottom-right corner, shown while zoomed or panned.</summary>
    private Rectangle ResetButtonRect
    {
        get
        {
            var size = TextRenderer.MeasureText("⟲ Reset view", ResetButtonFont);
            int w = size.Width + 12, h = size.Height + 6;
            return new Rectangle(ClientSize.Width - w - 8, ClientSize.Height - h - 8, w, h);
        }
    }

    private static readonly Font ResetButtonFont = new("Segoe UI Symbol", 8.5f);

    private void DrawResetButton(Graphics g)
    {
        if (!IsViewChanged || _image == null) return;
        var r = ResetButtonRect;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedRect(r, 4))
        using (var bg = new SolidBrush(_resetHover ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(200, 30, 30, 30)))
        {
            g.FillPath(bg, path);
        }
        g.SmoothingMode = SmoothingMode.None;
        TextRenderer.DrawText(g, "⟲ Reset view", ResetButtonFont, r, _resetHover ? Color.Black : Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void PaintCard(Graphics g)
    {
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

        if (!TryLayout(out var src, out var dstF, out double s)) return;
        var trim = CardCompositor.TrimRectFor(_image.Size);
        var dst = Rectangle.Round(dstF);

        // Only resample the visible part of the card, so deep zoom stays fast.
        var visible = RectangleF.Intersect(dstF, ClientRectangle);
        if (visible.Width <= 0 || visible.Height <= 0) return;
        var visibleSrc = new RectangleF(
            src.X + (visible.X - dstF.X) / (float)s, src.Y + (visible.Y - dstF.Y) / (float)s,
            visible.Width / (float)s, visible.Height / (float)s);

        // From 2 screen pixels per image pixel up, show sharp pixels instead of a smooth blur.
        bool pixelView = s >= 2;
        g.InterpolationMode = pixelView ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = pixelView ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
        using (var attrs = new System.Drawing.Imaging.ImageAttributes())
        {
            // TileFlipXY stops GDI+ from blending the image edge with transparent black when resampling a sub-rectangle.
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(_image,
                new[] { visible.Location, new PointF(visible.Right, visible.Top), new PointF(visible.Left, visible.Bottom) },
                visibleSrc, GraphicsUnit.Pixel, attrs);
        }
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (_showBleed)
        {
            // Shade the bleed and mark the trim line.
            var trimDst = new RectangleF(
                dstF.X + (float)(trim.X * s), dstF.Y + (float)(trim.Y * s),
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

            // Small badge in the top-right corner of the visible part of the card
            string badgeText = $"★ KMAT 0{_overcoatIndex}";
            using var badgeFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            var sz = g.MeasureString(badgeText, badgeFont);
            var badgeRect = new Rectangle((int)visible.Right - (int)sz.Width - 10, (int)visible.Top + 6, (int)sz.Width + 6, (int)sz.Height + 2);
            using (var bBg = new SolidBrush(Color.FromArgb(180, 20, 20, 20)))
                g.FillRectangle(bBg, badgeRect);
            using (var bFg = new SolidBrush(Color.Gold))
                g.DrawString(badgeText, badgeFont, bFg, badgeRect.X + 3, badgeRect.Y + 1);
        }
    }

    /// <summary>
    /// Where the card (or, without bleed, its face) is drawn: <paramref name="src"/> in image pixels,
    /// <paramref name="dst"/> in client pixels, <paramref name="scale"/> client pixels per image pixel.
    /// </summary>
    private bool TryLayout(out RectangleF src, out RectangleF dst, out double scale)
    {
        src = dst = RectangleF.Empty;
        scale = 0;
        if (_image == null) return false;
        const int margin = 8;
        var available = Rectangle.Inflate(ClientRectangle, -margin, -margin);
        if (available.Width <= 0 || available.Height <= 0) return false;

        src = _showBleed ? new RectangleF(0, 0, _image.Width, _image.Height) : CardCompositor.TrimRectFor(_image.Size);
        double fit = Math.Min(available.Width / (double)src.Width, available.Height / (double)src.Height);
        scale = fit * _zoom;
        float w = (float)(src.Width * scale), h = (float)(src.Height * scale);
        dst = new RectangleF(available.X + (available.Width - w) / 2f + _pan.X, available.Y + (available.Height - h) / 2f + _pan.Y, w, h);
        return true;
    }

    /// <summary>Keeps at least <see cref="KeepVisible"/> pixels of the card on screen.</summary>
    private void ClampPan()
    {
        if (!TryLayout(out _, out var dst, out _)) return;
        float centredX = dst.X - _pan.X, centredY = dst.Y - _pan.Y;
        float minX = KeepVisible - dst.Width - centredX, maxX = ClientSize.Width - KeepVisible - centredX;
        float minY = KeepVisible - dst.Height - centredY, maxY = ClientSize.Height - KeepVisible - centredY;
        _pan = new PointF(Math.Clamp(_pan.X, Math.Min(minX, maxX), Math.Max(minX, maxX)),
                          Math.Clamp(_pan.Y, Math.Min(minY, maxY), Math.Max(minY, maxY)));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!TryLayout(out _, out var before, out double scaleBefore)) return;
        float zoom = Math.Clamp(_zoom * MathF.Pow(WheelStep, e.Delta / 120f), MinZoom, MaxZoom);
        if (zoom == _zoom) return;

        // Keep the image point under the cursor where it is.
        float u = (float)((e.X - before.X) / scaleBefore), v = (float)((e.Y - before.Y) / scaleBefore);
        _zoom = zoom;
        if (zoom == MinZoom)
        {
            _pan = PointF.Empty;
        }
        else
        {
            TryLayout(out _, out var after, out double scaleAfter);
            _pan = new PointF(_pan.X + e.X - (after.X + (float)(u * scaleAfter)), _pan.Y + e.Y - (after.Y + (float)(v * scaleAfter)));
            ClampPan();
        }
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || _image == null) return;
        if (IsViewChanged && ResetButtonRect.Contains(e.Location))
        {
            _resetHover = false;
            Cursor = Cursors.Default;
            ResetView();
            return;
        }
        _dragStart = e.Location;
        _panAtDragStart = _pan;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start)
        {
            bool hover = IsViewChanged && ResetButtonRect.Contains(e.Location);
            if (hover != _resetHover)
            {
                _resetHover = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                Invalidate(ResetButtonRect);
            }
            return;
        }
        _pan = new PointF(_panAtDragStart.X + e.X - start.X, _panAtDragStart.Y + e.Y - start.Y);
        ClampPan();
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragStart = null;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) ResetView();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_resetHover)
        {
            _resetHover = false;
            Invalidate(ResetButtonRect);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampPan();
    }
}
