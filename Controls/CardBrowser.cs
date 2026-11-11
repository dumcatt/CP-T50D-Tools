using System.Drawing.Drawing2D;
using CPT50DTools.Cards;

namespace CPT50DTools.Controls;

/// <summary>
/// Owner-drawn, virtualized scrollable card grid with collapsible pack headers.
/// Uses GDI+ double-buffering and viewport-relative coordinates for tear-free,
/// artifact-free scrolling.
/// </summary>
public sealed class CardBrowser : ScrollableControl
{
    private const int HeaderHeight = 26;
    private const int Pad = 8;
    private const int LabelHeight = 18;

    private readonly List<CardPack> _packs = new();
    private readonly List<(Rectangle Rect, CardPack Pack)> _headerRects = new();
    private readonly List<(Rectangle Rect, CardItem Card)> _tileRects = new();
    private CardItem? _selected;
    private CardItem? _hover;
    private string _filter = "";
    private Size _thumbSize = new(110, 166);

    public event EventHandler? SelectedCardChanged;
    public event EventHandler<CardItem>? ThumbnailNeeded;

    public CardBrowser()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        AutoScroll = true;
        BackColor = Color.White;
        TabStop = true;
    }

    public IReadOnlyList<CardPack> Packs => _packs;

    public CardItem? SelectedCard
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
            SelectedCardChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Size ThumbSize
    {
        get => _thumbSize;
        set { _thumbSize = value; RelayoutAndInvalidate(); }
    }

    public string Filter
    {
        get => _filter;
        set { _filter = value ?? ""; RelayoutAndInvalidate(); }
    }

    public int TotalCards => _packs.Sum(p => p.Cards.Count);

    public void AddPacks(IEnumerable<CardPack> packs)
    {
        _packs.AddRange(packs);
        RelayoutAndInvalidate();
    }

    public void ClearPacks()
    {
        foreach (var c in _packs.SelectMany(p => p.Cards)) c.Thumbnail?.Dispose();
        _packs.Clear();
        _selected = null;
        _hover = null;
        RelayoutAndInvalidate();
        SelectedCardChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAllCollapsed(bool collapsed)
    {
        foreach (var p in _packs) p.Collapsed = collapsed;
        RelayoutAndInvalidate();
    }

    private bool Matches(CardItem c) =>
        _filter.Length == 0 || c.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
        c.Pack.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private Size TileSize => new(_thumbSize.Width + 8, _thumbSize.Height + 8 + LabelHeight);

    private void RelayoutAndInvalidate()
    {
        Relayout();
        Invalidate();
    }

    private void Relayout()
    {
        _headerRects.Clear();
        _tileRects.Clear();
        int width = Math.Max(ClientSize.Width, TileSize.Width + Pad * 2);
        int cols = Math.Max(1, (width - Pad) / (TileSize.Width + Pad));
        int y = 0;

        foreach (var pack in _packs)
        {
            var visibleCards = pack.Cards.Where(Matches).ToList();
            if (visibleCards.Count == 0 && _filter.Length > 0) continue;

            _headerRects.Add((new Rectangle(0, y, width, HeaderHeight), pack));
            y += HeaderHeight;
            if (pack.Collapsed) continue;

            y += Pad;
            for (int i = 0; i < visibleCards.Count; i++)
            {
                int col = i % cols, row = i / cols;
                var r = new Rectangle(Pad + col * (TileSize.Width + Pad), y + row * (TileSize.Height + Pad),
                    TileSize.Width, TileSize.Height);
                _tileRects.Add((r, visibleCards[i]));
            }
            int rows = (visibleCards.Count + cols - 1) / cols;
            y += rows * (TileSize.Height + Pad);
        }

        AutoScrollMinSize = new Size(0, y + Pad);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    private Point ToContent(Point client) => new(client.X - AutoScrollPosition.X, client.Y - AutoScrollPosition.Y);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_packs.Count == 0)
        {
            using var brush = new SolidBrush(SystemColors.GrayText);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("No cards loaded.\r\nUse File > Load card folder / Load card file.", Font, brush, ClientRectangle, sf);
            return;
        }

        int ox = AutoScrollPosition.X;
        int oy = AutoScrollPosition.Y;
        var clientView = ClientRectangle;

        using var headerFont = new Font(Font, FontStyle.Bold);

        // 1. Draw Headers
        foreach (var (rect, pack) in _headerRects)
        {
            var r = new Rectangle(rect.X + ox, rect.Y + oy, rect.Width, rect.Height);
            if (!r.IntersectsWith(clientView)) continue;
            DrawHeader(g, r, pack, headerFont);
        }

        // 2. Draw Tiles
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        foreach (var (rect, card) in _tileRects)
        {
            var r = new Rectangle(rect.X + ox, rect.Y + oy, rect.Width, rect.Height);
            if (!r.IntersectsWith(clientView)) continue;
            DrawTile(g, r, card);
        }
    }

    private void DrawHeader(Graphics g, Rectangle r, CardPack pack, Font bold)
    {
        using (var bg = new LinearGradientBrush(r, Color.FromArgb(250, 250, 250), Color.FromArgb(232, 232, 232), 90f))
        {
            g.FillRectangle(bg, r);
        }
        using (var pen = new Pen(Color.FromArgb(205, 205, 205)))
        {
            g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
        }

        // Expand/collapse chevron
        var c = new Point(r.Left + 12, r.Top + r.Height / 2);
        Point[] tri = pack.Collapsed
            ? new[] { new Point(c.X - 2, c.Y - 5), new Point(c.X + 4, c.Y), new Point(c.X - 2, c.Y + 5) }
            : new[] { new Point(c.X - 5, c.Y - 2), new Point(c.X + 5, c.Y - 2), new Point(c.X, c.Y + 4) };
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Color.FromArgb(80, 80, 80))) g.FillPolygon(b, tri);
        g.SmoothingMode = SmoothingMode.None;

        var textRect = new Rectangle(r.Left + 24, r.Top, r.Width - 30, r.Height);

        // Header text drawn with GDI+
        using (var brush = new SolidBrush(Color.FromArgb(30, 30, 30)))
        using (var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        })
        {
            g.DrawString(pack.Name, bold, brush, textRect, sf);
        }

        // Card count on the right
        int count = _filter.Length == 0 ? pack.Cards.Count : pack.Cards.Count(Matches);
        string countText = $"{count} card{(count == 1 ? "" : "s")}";
        using (var countBrush = new SolidBrush(SystemColors.GrayText))
        using (var sfRight = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap
        })
        {
            var rightRect = new Rectangle(r.Left, r.Top, r.Width - 10, r.Height);
            g.DrawString(countText, Font, countBrush, rightRect, sfRight);
        }
    }

    private void DrawTile(Graphics g, Rectangle r, CardItem card)
    {
        bool selected = card == _selected;
        bool hover = card == _hover;

        if (selected)
        {
            using var b = new SolidBrush(Color.FromArgb(204, 232, 255));
            g.FillRectangle(b, r);
            using var p = new Pen(Color.FromArgb(153, 209, 255));
            g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
        else if (hover)
        {
            using var b = new SolidBrush(Color.FromArgb(229, 243, 255));
            g.FillRectangle(b, r);
        }

        var thumbArea = new Rectangle(r.X + 4, r.Y + 4, _thumbSize.Width, _thumbSize.Height);
        var thumb = card.Thumbnail;
        if (thumb != null)
        {
            double s = Math.Min((double)thumbArea.Width / thumb.Width, (double)thumbArea.Height / thumb.Height);
            int w = (int)(thumb.Width * s), h = (int)(thumb.Height * s);
            var dst = new Rectangle(thumbArea.X + (thumbArea.Width - w) / 2, thumbArea.Y + (thumbArea.Height - h) / 2, w, h);
            g.DrawImage(thumb, dst);
            using var p = new Pen(Color.FromArgb(160, 160, 160));
            g.DrawRectangle(p, dst.X, dst.Y, dst.Width - 1, dst.Height - 1);
        }
        else
        {
            using (var b = new SolidBrush(Color.FromArgb(238, 238, 238))) g.FillRectangle(b, thumbArea);
            using (var p = new Pen(Color.FromArgb(190, 190, 190)))
                g.DrawRectangle(p, thumbArea.X, thumbArea.Y, thumbArea.Width - 1, thumbArea.Height - 1);

            using var textBrush = new SolidBrush(card.ThumbnailFailed ? Color.Firebrick : SystemColors.GrayText);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(card.ThumbnailFailed ? "Error" : "Loading...", Font, textBrush, thumbArea, sf);

            if (!card.ThumbnailFailed) ThumbnailNeeded?.Invoke(this, card);
        }

        var labelRect = new Rectangle(r.X + 2, thumbArea.Bottom + 2, r.Width - 4, LabelHeight);
        using (var labelBrush = new SolidBrush(Color.Black))
        using (var sfCenter = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        })
        {
            g.DrawString(card.Name, Font, labelBrush, labelRect, sfCenter);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        var p = ToContent(e.Location);

        foreach (var (rect, pack) in _headerRects)
        {
            if (rect.Contains(p))
            {
                pack.Collapsed = !pack.Collapsed;
                RelayoutAndInvalidate();
                return;
            }
        }
        foreach (var (rect, card) in _tileRects)
        {
            if (rect.Contains(p))
            {
                SelectedCard = card;
                return;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToContent(e.Location);
        CardItem? hover = null;
        foreach (var (rect, card) in _tileRects)
            if (rect.Contains(p)) { hover = card; break; }
        bool overHeader = _headerRects.Any(h => h.Rect.Contains(p));
        Cursor = hover != null || overHeader ? Cursors.Hand : Cursors.Default;
        if (hover != _hover)
        {
            _hover = hover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null) { _hover = null; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    private const int WM_VSCROLL = 0x0115;
    private const int WM_HSCROLL = 0x0114;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is WM_VSCROLL or WM_HSCROLL)
        {
            Invalidate();
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_tileRects.Count == 0) return;
        int idx = _selected == null ? -1 : _tileRects.FindIndex(t => t.Card == _selected);
        int cols = Math.Max(1, (Math.Max(ClientSize.Width, TileSize.Width + Pad * 2) - Pad) / (TileSize.Width + Pad));
        int next = e.KeyCode switch
        {
            Keys.Right => idx + 1,
            Keys.Left => idx - 1,
            Keys.Down => idx < 0 ? 0 : idx + cols,
            Keys.Up => idx - cols,
            _ => int.MinValue,
        };
        if (next == int.MinValue) return;
        next = Math.Clamp(next, 0, _tileRects.Count - 1);
        SelectedCard = _tileRects[next].Card;
        EnsureVisible(_tileRects[next].Rect);
        e.Handled = true;
    }

    private void EnsureVisible(Rectangle contentRect)
    {
        int top = -AutoScrollPosition.Y;
        if (contentRect.Top < top) AutoScrollPosition = new Point(0, contentRect.Top - Pad);
        else if (contentRect.Bottom > top + ClientSize.Height)
            AutoScrollPosition = new Point(0, contentRect.Bottom - ClientSize.Height + Pad);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var c in _packs.SelectMany(p => p.Cards)) c.Thumbnail?.Dispose();
        base.Dispose(disposing);
    }
}
