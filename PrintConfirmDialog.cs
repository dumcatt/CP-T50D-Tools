using CPT50DTools.Cards;
using CPT50DTools.Controls;

namespace CPT50DTools;

public sealed class PrintConfirmDialog : Form
{
    // Print queue browsing: the caller's image for the card it passed in, and images this dialog
    // rendered itself for the other queued cards (disposed with the dialog).
    private readonly IReadOnlyList<CardItem> _queue;
    private readonly CardItem _initialCard;
    private readonly Image _initialImage;
    private readonly Dictionary<CardItem, Bitmap> _rendered = new();
    private readonly PreviewBox _preview;
    private Label _cardValue = null!, _sourceValue = null!, _resolutionValue = null!;
    private readonly Label? _counter;
    private int _index;
    private int _loadVersion;

    public PrintConfirmDialog(
        CardItem card,
        Image cardImage,
        int overcoatIndex,
        string overcoatName,
        int density,
        bool preheat,
        string printerStatus,
        IReadOnlyList<CardItem>? queue = null)
    {
        _queue = queue is { Count: > 1 } ? queue : new[] { card };
        _initialCard = card;
        _initialImage = cardImage;
        _index = Math.Max(0, IndexOf(card));
        int queueCount = _queue.Count;
        bool isQueue = queueCount > 1;
        Text = isQueue ? "Confirm Print Queue - Mitsubishi CP-T50D" : "Confirm Print - Mitsubishi CP-T50D";
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(760, 620);
        MinimumSize = new Size(680, 540);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(12),
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Left: Preview
        var previewGroup = new GroupBox
        {
            Text = "Card Preview",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };
        _preview = new PreviewBox
        {
            Dock = DockStyle.Fill,
            Image = cardImage,
            OvercoatIndex = overcoatIndex,
            // The printer receives the full image, bleed included; the dashed line marks the trim.
            ShowBleed = true,
        };
        previewGroup.Controls.Add(_preview);

        // ◀ i/N ▶ to look through the queued cards, bottom left under the preview (as in the main window).
        if (isQueue)
        {
            Button NavButton(string text, int delta)
            {
                var b = new Button
                {
                    Text = text,
                    Size = new Size(24, 22),
                    Margin = new Padding(0, 1, 2, 1),
                    Padding = new Padding(0),
                    Font = new Font("Segoe UI Symbol", 8f),
                    FlatStyle = FlatStyle.System,
                    TabStop = false,
                };
                b.Click += (_, _) => Step(delta);
                return b;
            }
            _counter = new Label { AutoSize = true, Margin = new Padding(2, 5, 4, 0) };
            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0),
            };
            nav.Controls.AddRange(new Control[] { NavButton("◀", -1), _counter, NavButton("▶", +1) });
            previewGroup.Controls.Add(nav);
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Left or Keys.Right && !e.Control && !e.Alt)
                {
                    Step(e.KeyCode == Keys.Left ? -1 : +1);
                    e.Handled = true;
                }
            };
        }
        mainLayout.Controls.Add(previewGroup, 0, 0);

        // Right: Job Details
        var detailsGroup = new GroupBox
        {
            Text = "Print Job Summary",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };
        var detailsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = isQueue ? 8 : 7,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Label AddRow(string label, string val, bool bold = false)
        {
            var lbl = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = SystemColors.ControlDarkDark,
                Margin = new Padding(0, 6, 4, 6),
            };
            var valLbl = new Label
            {
                Text = val,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = bold ? new Font("Segoe UI", 9f, FontStyle.Bold) : new Font("Segoe UI", 9f),
                Margin = new Padding(0, 6, 0, 6),
            };
            detailsLayout.Controls.Add(lbl);
            detailsLayout.Controls.Add(valLbl);
            return valLbl;
        }

        if (isQueue) AddRow("Print queue:", $"{queueCount} cards, printed in browser order", bold: true);
        _cardValue = AddRow(isQueue ? "Previewing:" : "Card:", card.Name, bold: !isQueue);
        _sourceValue = AddRow("Source:", Path.GetFileName(card.Pack.Name));
        _resolutionValue = AddRow("Resolution:", $"{cardImage.Width} x {cardImage.Height} px");
        AddRow("Print Finish:", overcoatName, bold: true);
        AddRow("Density:", overcoatIndex == 0 ? $"{density:+#;-#;0}" : "(Locked by overcoat)");
        AddRow("Pre-Heat Head:", preheat ? "Enabled" : "Disabled");
        AddRow("Printer:", printerStatus);

        detailsGroup.Controls.Add(detailsLayout);
        mainLayout.Controls.Add(detailsGroup, 1, 0);

        // Bottom Button Bar
        // Right-aligned, same-size buttons; the row sizes itself so nothing is clipped at any DPI.
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
            Margin = new Padding(3, 8, 3, 0),
        };

        Button DialogButton(string text, DialogResult result, bool primary) => new()
        {
            Text = text,
            DialogResult = result,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(110, 34),
            Padding = new Padding(10, 0, 10, 0),
            Font = new Font("Segoe UI", 10f, primary ? FontStyle.Bold : FontStyle.Regular),
        };

        var continueBtn = DialogButton(isQueue ? $"Print {queueCount} Cards" : "Continue", DialogResult.OK, primary: true);
        continueBtn.Margin = new Padding(0);
        var cancelBtn = DialogButton("Cancel", DialogResult.Cancel, primary: false);
        cancelBtn.Margin = new Padding(0, 0, 10, 0);

        buttonPanel.Controls.Add(continueBtn);
        buttonPanel.Controls.Add(cancelBtn);

        mainLayout.Controls.Add(buttonPanel, 0, 1);
        mainLayout.SetColumnSpan(buttonPanel, 2);

        AcceptButton = continueBtn;
        CancelButton = cancelBtn;

        Controls.Add(mainLayout);
        UpdateCounter();
    }

    private int IndexOf(CardItem card)
    {
        for (int i = 0; i < _queue.Count; i++)
            if (_queue[i] == card) return i;
        return -1;
    }

    private void UpdateCounter()
    {
        if (_counter != null) _counter.Text = $"{_index + 1}/{_queue.Count}";
    }

    /// <summary>Shows the previous / next queued card (wrapping around), rendering it the first time.</summary>
    private async void Step(int delta)
    {
        _index = ((_index + delta) % _queue.Count + _queue.Count) % _queue.Count;
        var card = _queue[_index];
        int version = ++_loadVersion;
        UpdateCounter();
        _cardValue.Text = card.Name;
        _sourceValue.Text = Path.GetFileName(card.Pack.Name);

        Image? image = card == _initialCard ? _initialImage : _rendered.GetValueOrDefault(card);
        if (image == null)
        {
            _preview.Image = null;
            _preview.Placeholder = "Loading...";
            _resolutionValue.Text = "...";
            try
            {
                var bmp = await Task.Run(card.LoadFull);
                if (IsDisposed) { bmp.Dispose(); return; }
                // Keep it even if the user has moved on, so going back is instant.
                if (!_rendered.TryAdd(card, bmp)) bmp.Dispose();
                image = _rendered[card];
            }
            catch (Exception ex)
            {
                if (version != _loadVersion || IsDisposed) return;
                _preview.Placeholder = "Preview\nnot\navailable";
                _resolutionValue.Text = $"Failed to load: {ex.Message}";
                return;
            }
            if (version != _loadVersion) return;
        }
        _preview.Image = image;
        _resolutionValue.Text = $"{image.Width} x {image.Height} px";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _preview.Image = null;
        foreach (var bmp in _rendered.Values) bmp.Dispose();
        _rendered.Clear();
        base.OnFormClosed(e);
    }
}
