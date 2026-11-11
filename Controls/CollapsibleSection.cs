namespace CPT50DTools.Controls;

/// <summary>
/// A titled section whose body folds away when its header is clicked. The header is drawn with
/// <see cref="SectionHeader"/>, the same renderer as the card browser's pack headers.
/// Put content in <see cref="Body"/>; enable/disable <see cref="Body"/> rather than the section so the
/// header stays clickable.
/// </summary>
public sealed class CollapsibleSection : TableLayoutPanel
{
    private readonly HeaderControl _header;
    private bool _collapsed;

    public CollapsibleSection(string title)
    {
        Title = title;
        ColumnCount = 1;
        RowCount = 2;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 3, 0, 3);
        Padding = new Padding(1); // room for the border drawn in OnPaint
        BackColor = Color.White;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _header = new HeaderControl(this) { Dock = DockStyle.Fill, Height = SectionHeader.Height, Margin = new Padding(0) };

        Body = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 4, 6, 6),
            Margin = new Padding(0),
            BackColor = Color.White,
        };

        Controls.Add(_header, 0, 0);
        Controls.Add(Body, 0, 1);
    }

    public string Title { get; }

    /// <summary>Optional grey text on the right of the header (like the browser's card count).</summary>
    public string? Note
    {
        get => _header.Note;
        set { _header.Note = value; _header.Invalidate(); }
    }

    /// <summary>Container for the section's content.</summary>
    public Panel Body { get; }

    /// <summary>Raised when the user (or code) collapses or expands the section.</summary>
    public event EventHandler? CollapsedChanged;

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            Body.Visible = !value;
            _header.Invalidate();
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(160, 160, 160));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Invalidate();
    }

    /// <summary>Clickable, focusable header bar.</summary>
    private sealed class HeaderControl : Control
    {
        private readonly CollapsibleSection _owner;
        private bool _hot;

        public HeaderControl(CollapsibleSection owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public string? Note { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string Text
        {
            get => _owner.Title;
            set { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var bold = new Font(Font, FontStyle.Bold);
            SectionHeader.Draw(e.Graphics, ClientRectangle, _owner.Title, _owner.Collapsed, bold, Font,
                Note, hot: _hot, focused: Focused && ShowFocusCues);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            _owner.Collapsed = !_owner.Collapsed;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                _owner.Collapsed = !_owner.Collapsed;
                e.Handled = true;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
