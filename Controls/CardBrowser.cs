using System.Drawing.Drawing2D;
using System.Windows.Forms.VisualStyles;
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
    private const int CheckSize = 16;

    private CardItem? _selected;
    // Every selected card; always contains _selected when that is set.
    private readonly HashSet<CardItem> _selection = new();
    // Where Shift+click / Shift+arrow ranges start.
    private CardItem? _anchor;
    private CardItem? _hover;
    private string _filter = "";
    private Size _thumbSize = new(110, 166);

    /// <summary>The focused card (the one shown in the preview) changed.</summary>
    public event EventHandler? SelectedCardChanged;
    /// <summary>The set of selected cards changed.</summary>
    public event EventHandler? SelectionChanged;
    public event EventHandler<CardItem>? ThumbnailNeeded;

    public CardBrowser()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        AutoScroll = true;
        BackColor = Color.White;
        TabStop = true;
    }

    /// <summary>The focused card. Setting it selects only that card.</summary>
    public CardItem? SelectedCard
    {
        get => _selected;
        set
        {
            _anchor = value;
            SetSelection(value == null ? Array.Empty<CardItem>() : new[] { value }, value);
        }
    }

    /// <summary>All selected cards, in browser order (pack order, then card order).</summary>
    public IReadOnlyList<CardItem> SelectedCards =>
        _selection.Count == 0 ? Array.Empty<CardItem>() : _packs.SelectMany(p => p.Cards).Where(_selection.Contains).ToList();

    /// <summary>Shows another card of the current selection in the preview without changing the selection.</summary>
    public void FocusCard(CardItem card)
    {
        if (!_selection.Contains(card)) return;
        SetFocused(card);
        int i = _tileRects.FindIndex(t => t.Card == card);
        if (i >= 0) EnsureVisible(_tileRects[i].Rect);
    }

    private void SetFocused(CardItem? card)
    {
        if (_selected == card) return;
        _selected = card;
        Invalidate();
        SelectedCardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetSelection(IEnumerable<CardItem> cards, CardItem? focus)
    {
        var next = new HashSet<CardItem>(cards);
        if (focus != null) next.Add(focus);
        bool changed = !next.SetEquals(_selection);
        if (changed)
        {
            _selection.Clear();
            _selection.UnionWith(next);
        }
        Invalidate();
        SetFocused(focus);
        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ctrl+click / checkbox: adds or removes one card, keeping the rest of the selection.</summary>
    private void ToggleCard(CardItem card)
    {
        _anchor = card;
        if (!_selection.Contains(card))
        {
            SetSelection(_selection.Append(card), card);
            return;
        }
        var rest = _selection.Where(c => c != card).ToList();
        // Keep showing the focused card if it is still selected; otherwise the nearest one in browser order.
        var focus = _selected != card && _selected != null ? _selected
            : SelectedCards.Where(c => c != card).OrderBy(c => Math.Abs(DisplayIndex(c) - DisplayIndex(card))).FirstOrDefault();
        SetSelection(rest, focus);
    }

    /// <summary>Shift+click / Shift+arrow: selects the visible cards from the anchor to <paramref name="card"/>.</summary>
    private void SelectRange(CardItem card, bool addToSelection)
    {
        int a = _anchor == null ? -1 : _tileRects.FindIndex(t => t.Card == _anchor);
        int b = _tileRects.FindIndex(t => t.Card == card);
        if (a < 0 || b < 0)
        {
            SelectedCard = card;
            return;
        }
        var range = _tileRects.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1).Select(t => t.Card);
        SetSelection(addToSelection ? _selection.Concat(range) : range, card);
    }

    private int DisplayIndex(CardItem card)
    {
        int i = 0;
        foreach (var c in _packs.SelectMany(p => p.Cards))
        {
            if (c == card) return i;
            i++;
        }
        return int.MaxValue;
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
        _selection.Clear();
        _anchor = null;
        _hover = null;
        RelayoutAndInvalidate();
        SelectedCardChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAllCollapsed(bool collapsed)
    {
        foreach (var p in _packs) p.Collapsed = collapsed;
        RelayoutAndInvalidate();
    }

    /// <summary>
    /// Matches the card file name, pack name, and the printed serial number, illustrator and title
    /// (including the user's edits). Serial matching ignores '-', '/' and spaces, so
    /// "SV05-N003-005", "n003-005" and "sv05n003005" all find the same card.
    /// </summary>
    private bool Matches(CardItem c)
    {
        if (_filter.Length == 0 ||
            c.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            c.Pack.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            return true;

        var info = c.CatalogInfo();
        if (!info.FromCatalog) return false;
        var text = CardTextOverrides.Get(c.Name, info);
        if (text.Illustrator.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            text.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            return true;

        string compactFilter = Compact(_filter);
        return compactFilter.Length > 0 && Compact(text.Serial).Contains(compactFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string s) =>
        string.Concat(s.Where(ch => ch is not ('-' or '/' or ' ' or '　' or '_')));

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
            g.DrawString("No cards loaded.\r\nUse File > Load game data folder / Load card file.", Font, brush, ClientRectangle, sf);
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
        int count = _filter.Length == 0 ? pack.Cards.Count : pack.Cards.Count(Matches);
        SectionHeader.Draw(g, r, pack.Name, pack.Collapsed, bold, Font, $"{count} card{(count == 1 ? "" : "s")}");
    }

    private void DrawTile(Graphics g, Rectangle r, CardItem card)
    {
        bool selected = _selection.Contains(card);
        bool focused = card == _selected;
        bool hover = card == _hover;

        if (selected)
        {
            using var b = new SolidBrush(Color.FromArgb(204, 232, 255));
            g.FillRectangle(b, r);
            // The focused card (shown in the preview) gets a stronger outline when several are selected.
            using var p = new Pen(focused && _selection.Count > 1 ? Color.FromArgb(0, 120, 215) : Color.FromArgb(153, 209, 255), focused && _selection.Count > 1 ? 2f : 1f);
            g.DrawRectangle(p, r.X + (p.Width > 1 ? 1 : 0), r.Y + (p.Width > 1 ? 1 : 0), r.Width - 1 - (p.Width > 1 ? 1 : 0), r.Height - 1 - (p.Width > 1 ? 1 : 0));
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

        // Checkbox for adding the card to a multi-selection: shown on hover, and on every card once
        // more than one is selected.
        if (hover || (selected && _selection.Count > 1))
        {
            var box = CheckRect(r);
            using (var bg = new SolidBrush(Color.FromArgb(220, Color.White))) g.FillRectangle(bg, Rectangle.Inflate(box, 2, 2));
            CheckBoxRenderer.DrawCheckBox(g, box.Location, selected ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
        }
    }

    /// <summary>Checkbox in the top-left corner of a tile (same coordinate space as <paramref name="tile"/>).</summary>
    private static Rectangle CheckRect(Rectangle tile) => new(tile.X + 7, tile.Y + 7, CheckSize - 3, CheckSize - 3);

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
            if (!rect.Contains(p)) continue;
            var mods = ModifierKeys;
            if ((mods & Keys.Shift) != 0)
                SelectRange(card, addToSelection: (mods & Keys.Control) != 0);
            else if ((mods & Keys.Control) != 0 || Rectangle.Inflate(CheckRect(rect), 3, 3).Contains(p))
                ToggleCard(card);
            else
                SelectedCard = card;
            return;
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
        if (e.KeyCode == Keys.A && e.Control)
        {
            // Select every visible (filtered, expanded) card.
            SetSelection(_tileRects.Select(t => t.Card), _selected != null && _tileRects.Any(t => t.Card == _selected) ? _selected : _tileRects[0].Card);
            e.Handled = true;
            return;
        }
        if (e.KeyCode == Keys.Escape && _selection.Count > 1)
        {
            SelectedCard = _selected;
            e.Handled = true;
            return;
        }
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
        if (e.Shift) SelectRange(_tileRects[next].Card, addToSelection: false);
        else SelectedCard = _tileRects[next].Card;
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
