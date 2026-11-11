using System.Reflection;
using CPT50DTools.Cards;
using CPT50DTools.Controls;
using CPT50DTools.Printer;

namespace CPT50DTools;

public sealed class MainForm : Form
{
    private static readonly string[] OvercoatOptions =
    {
        "Glossy (no pattern)",
        "Pattern 1 - SDVX Crest + Laurel (kmat_01)",
        "Pattern 2 - SDVX Diamond Emblem (kmat_02)",
        "Pattern 3 - SDVX Crest Emblem (kmat_03)",
        "Pattern 4 - Card Frame Overcoat (kmat_04)",
        "Pattern 5 - PUR Radial Burst (kmat_05)",
        "Pattern 6 - SDVX Crest, Landscape (kmat_06)",
    };

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ThumbnailLoader _thumbs = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 16000,
        InitialDelay = 350,
        ReshowDelay = 200,
        IsBalloon = false,
        ShowAlways = true,
    };
    private readonly string _version;

    // Menu
    private MenuStrip _menu = null!;
    private ToolStripMenuItem _miExportPng = null!;

    // Left side
    private TabControl _leftTabs = null!;
    private TextBox _searchBox = null!;
    private CardBrowser _browser = null!;
    private TextBox _logBox = null!;

    // Right side
    private PreviewBox _preview = null!;
    private Label _resolutionLabel = null!;
    private FlowLayoutPanel _selectionNav = null!;
    private Button _prevCardButton = null!;
    private Button _nextCardButton = null!;
    private Label _selectionCounter = null!;
    private ComboBox _overcoatCombo = null!;
    private NumericUpDown _densityUpDown = null!;
    private CheckBox _preheatCheck = null!;
    private NumericUpDown _offsetUpDown = null!;
    private CheckBox _bleedCheck = null!;
    private CheckBox _copyrightCheck = null!;
    private CheckBox _frameCheck = null!;
    private CheckBox _cardTextCheck = null!;
    private CollapsibleSection _textBox = null!;
    private CollapsibleSection _optionsSection = null!;
    private TextBox _illustratorText = null!;
    private TextBox _titleText = null!;
    private TextBox _serialText = null!;
    private Button _resetTextButton = null!;
    private readonly System.Windows.Forms.Timer _textTimer = new() { Interval = 350 };
    private bool _loadingText;
    private Button _defaultsButton = null!;
    private Button _printButton = null!;

    // Status bar
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _printerDot = null!;
    private ToolStripStatusLabel _printerLabel = null!;

    private readonly System.Windows.Forms.Timer _repaintTimer = new() { Interval = 100 };
    private volatile bool _thumbsDirty;

    // Printer
    private PrinterService? _printer;
    private PrinterStatusDialog? _statusDialog;
    private bool _printing;
    private bool _cancelQueue;
    private int _queueTotal, _queuePosition;

    private Bitmap? _currentImage;
    private int _selectionVersion;
    private CancellationTokenSource? _loadCts;

    public MainForm(string[]? startupPaths = null)
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
        _version = $"v{v.Major}.{v.Minor}.{v.Build}";

        BuildUi();
        ApplySettingsToControls();

        // Initialize managers
        if (_settings.PrinterDataFolder != null)
            OvercoatManager.Instance.SetDataDirectory(_settings.PrinterDataFolder);

        _thumbs.ThumbnailLoaded += _ => _thumbsDirty = true;
        _repaintTimer.Tick += (_, _) =>
        {
            if (!_thumbsDirty) return;
            _thumbsDirty = false;
            _browser.Invalidate();
        };
        _repaintTimer.Start();

        SetPrinterState(PrinterState.SdkNotLoaded, "Printer SDK not loaded");
        Log($"CP-T50D Tools {_version} started.");
        if (_settings.PrinterSdkFolder != null) Log($"Printer SDK folder: {_settings.PrinterSdkFolder}");
        if (_settings.PrinterDataFolder != null) Log($"Printer data folder: {_settings.PrinterDataFolder}");

        Shown += async (_, _) =>
        {
            if (_settings.PrinterSdkFolder != null && Directory.Exists(_settings.PrinterSdkFolder))
                StartPrinter(_settings.PrinterSdkFolder, showErrors: false);
            if (startupPaths is { Length: > 0 }) await LoadPathsAsync(startupPaths);
        };
    }

    #region UI construction

    private void BuildUi()
    {
        SuspendLayout();
        Text = $"CP-T50D Tools {_version}";
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(1180, 780);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Control;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ---------- Menu ----------
        _menu = new MenuStrip { RenderMode = ToolStripRenderMode.System, Padding = new Padding(4, 2, 0, 2) };

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("Load game &data folder...", null, (_, _) => LoadDataFolder(), Keys.Control | Keys.Shift | Keys.O));
        file.DropDownItems.Add(new ToolStripMenuItem("Load card f&ile...", null, (_, _) => LoadCardFile(), Keys.Control | Keys.O));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Clear loaded cards", null, (_, _) => ClearCards()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close(), Keys.Alt | Keys.F4));

        var options = new ToolStripMenuItem("&Options");
        options.DropDownItems.Add(new ToolStripMenuItem("Load Printer &SDK folder...", null, (_, _) => LoadPrinterSdkFolder()));
        options.DropDownItems.Add(new ToolStripMenuItem("Print &test card", null, (_, _) => PrintTestCard()));
        options.DropDownItems.Add(new ToolStripMenuItem("Printer s&tatus", null, (_, _) => ShowPrinterStatus()));
        options.DropDownItems.Add(new ToolStripSeparator());
        _miExportPng = new ToolStripMenuItem("&Export selected PNG...", null, (_, _) => ExportSelectedPng(), Keys.Control | Keys.E) { Enabled = false };
        options.DropDownItems.Add(_miExportPng);

        var versionLabel = new ToolStripLabel(_version)
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = SystemColors.GrayText,
        };
        _menu.Items.AddRange(new ToolStripItem[] { file, options, versionLabel });
        MainMenuStrip = _menu;

        // ---------- Status bar ----------
        var status = new StatusStrip { SizingGrip = true, RenderMode = ToolStripRenderMode.System };
        _statusLabel = new ToolStripStatusLabel("Ready to go") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _printerLabel = new ToolStripStatusLabel("Printer offline");
        _printerDot = new ToolStripStatusLabel("●") { Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        status.Items.AddRange(new ToolStripItem[] { _statusLabel, _printerLabel, _printerDot });

        // ---------- Split ----------
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 300,
            SplitterWidth = 5,
            Padding = new Padding(2),
        };

        split.Panel1.Controls.Add(BuildLeftPanel());
        split.Panel2.Controls.Add(BuildRightPanel());

        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(_menu);

        Load += (_, _) =>
        {
            split.Panel2MinSize = 310;
            split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Width - 370);
        };

        ResumeLayout(true);
    }

    private Control BuildLeftPanel()
    {
        _leftTabs = new TabControl { Dock = DockStyle.Fill };

        // Card browser tab
        var cardsTab = new TabPage("Card Browser") { Padding = new Padding(2), UseVisualStyleBackColor = true };

        _searchBox = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search cards, packs, serial numbers, illustrators or titles", BorderStyle = BorderStyle.FixedSingle };
        _searchBox.TextChanged += (_, _) => _browser.Filter = _searchBox.Text.Trim();
        _toolTip.SetToolTip(_searchBox, "Filter by card or pack name, serial number (e.g. SV05-N003-005 or n003-005), illustrator or title");

        // Top bar above browser: Expand/Collapse on left, Thumbnail size slider on the RIGHT
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(2, 2, 2, 2),
        };

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        var expandAll = new LinkLabel { Text = "Expand all", AutoSize = true, Margin = new Padding(2, 6, 8, 2) };
        expandAll.LinkClicked += (_, _) => _browser.SetAllCollapsed(false);
        var collapseAll = new LinkLabel { Text = "Collapse all", AutoSize = true, Margin = new Padding(2, 6, 8, 2) };
        collapseAll.LinkClicked += (_, _) => _browser.SetAllCollapsed(true);
        leftButtons.Controls.AddRange(new Control[] { expandAll, collapseAll });

        var rightSliderPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        var sizeLabel = new Label { Text = "Thumbnail size:", AutoSize = true, Margin = new Padding(4, 6, 2, 2) };
        var sizeTrack = new TrackBar
        {
            Minimum = 60, Maximum = 200, Value = 110, TickStyle = TickStyle.None,
            AutoSize = false, Height = 22, Width = 120, Margin = new Padding(0, 2, 2, 0),
            BackColor = SystemColors.Window,
        };
        sizeTrack.ValueChanged += (_, _) =>
            _browser.ThumbSize = new Size(sizeTrack.Value, (int)(sizeTrack.Value * 1074 / 714.0));
        _toolTip.SetToolTip(sizeTrack, "Adjust the card thumbnail size in the browser");

        rightSliderPanel.Controls.AddRange(new Control[] { sizeLabel, sizeTrack });

        toolbar.Controls.Add(rightSliderPanel);
        toolbar.Controls.Add(leftButtons);

        var browserBorder = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(0) };
        _browser = new CardBrowser { Dock = DockStyle.Fill, AllowDrop = true };
        _browser.SelectedCardChanged += (_, _) => OnSelectedCardChanged();
        _browser.SelectionChanged += (_, _) => UpdateSelectionNav();
        _toolTip.SetToolTip(_browser,
            "Click: select a card   Ctrl+click or tile checkbox: add / remove   Shift+click: select a range\n" +
            "Ctrl+A: select all shown cards   Esc: back to one card   Several selected cards print as a queue.");
        _browser.ThumbnailNeeded += (_, card) => _thumbs.Request(card);
        _browser.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        _browser.DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await LoadPathsAsync(paths);
        };
        browserBorder.Controls.Add(_browser);

        cardsTab.Controls.Add(browserBorder);
        cardsTab.Controls.Add(toolbar);
        cardsTab.Controls.Add(_searchBox);

        // Log tab
        var logTab = new TabPage("Log") { Padding = new Padding(2), UseVisualStyleBackColor = true };
        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Font = new Font("Consolas", 9f),
            WordWrap = false,
        };
        logTab.Controls.Add(_logBox);

        _leftTabs.TabPages.Add(cardsTab);
        _leftTabs.TabPages.Add(logTab);
        return _leftTabs;
    }

    private Control BuildRightPanel()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var previewTab = new TabPage("Preview") { Padding = new Padding(2), UseVisualStyleBackColor = true };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // preview
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // resolution
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // card text
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // print options (incl. reset)
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // print button

        _preview = new PreviewBox { Dock = DockStyle.Fill, Margin = new Padding(0) };

        // Card text (illustrator / title / serial printed on the card)
        _textBox = new CollapsibleSection("Card text") { Collapsed = _settings.CardTextCollapsed };
        _textBox.Body.Enabled = false;
        _textBox.CollapsedChanged += (_, _) => { _settings.CardTextCollapsed = _textBox.Collapsed; _settings.Save(); };
        var textLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        textLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        textLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _illustratorText = new TextBox { Dock = DockStyle.Fill };
        _titleText = new TextBox { Dock = DockStyle.Fill };
        _serialText = new TextBox { Dock = DockStyle.Fill };
        foreach (var tb in new[] { _illustratorText, _titleText, _serialText })
            tb.TextChanged += (_, _) => { if (!_loadingText) { _textTimer.Stop(); _textTimer.Start(); } };
        _textTimer.Tick += (_, _) => { _textTimer.Stop(); ApplyCardText(); };
        _toolTip.SetToolTip(_illustratorText, "Illustrator name printed on the card.\nDefaults to the value in the game's genesis_card.xml.");
        _toolTip.SetToolTip(_titleText, "Card title printed on the card (not shown on cards with only an ILLUST tag).\nDefaults to the value in the game's genesis_card.xml.");
        _toolTip.SetToolTip(_serialText, "Serial number printed at the bottom-left of the card.");
        _resetTextButton = new Button { Text = "Reset to default", AutoSize = true, Anchor = AnchorStyles.Right, Enabled = false };
        _resetTextButton.Click += (_, _) => ResetCardText();
        _toolTip.SetToolTip(_resetTextButton, "Restore the text from genesis_card.xml for this card");
        AddOptionRow(textLayout, "Illustrator:", _illustratorText);
        AddOptionRow(textLayout, "Title:", _titleText);
        AddOptionRow(textLayout, "Serial:", _serialText);
        textLayout.Controls.Add(_resetTextButton);
        textLayout.SetColumnSpan(_resetTextButton, 2);
        _textBox.Body.Controls.Add(textLayout);

        _resolutionLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = false,
            Height = 20,
            ForeColor = SystemColors.GrayText,
            Text = "No card selected",
            AutoEllipsis = true,
        };

        // ◀ 1/N ▶ under the preview when several cards are selected; the resolution stays on the right.
        Button NavButton(string text) => new()
        {
            Text = text,
            Size = new Size(24, 22),
            Margin = new Padding(0, 1, 2, 1),
            Padding = new Padding(0),
            Font = new Font("Segoe UI Symbol", 8f),
            FlatStyle = FlatStyle.System,
        };
        _prevCardButton = NavButton("◀");
        _nextCardButton = NavButton("▶");
        _prevCardButton.Click += (_, _) => StepSelection(-1);
        _nextCardButton.Click += (_, _) => StepSelection(+1);
        _toolTip.SetToolTip(_prevCardButton, "Previous selected card");
        _toolTip.SetToolTip(_nextCardButton, "Next selected card");
        _selectionCounter = new Label { AutoSize = true, Margin = new Padding(2, 5, 4, 0), ForeColor = SystemColors.ControlText };
        _selectionNav = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Visible = false,
        };
        _selectionNav.Controls.AddRange(new Control[] { _prevCardButton, _selectionCounter, _nextCardButton });
        _bleedCheck = new CheckBox
        {
            Text = "Show bleed area",
            AutoSize = true,
            Dock = DockStyle.Left,
            Padding = new Padding(4, 3, 0, 0),
        };
        _bleedCheck.CheckedChanged += (_, _) =>
        {
            _preview.ShowBleed = _bleedCheck.Checked;
            SaveControlsToSettings();
        };
        _toolTip.SetToolTip(_bleedCheck,
            "Show the whole 714x1074 print area instead of just the card face.\n" +
            "The bleed (shaded) is printed past the card edge and trimmed off; the dashed line is the trim line.\n" +
            "Useful for positioning custom card art. The printed/exported image always includes the bleed.");

        // Left to right: ◀ 1/N ▶ (several cards selected), Show bleed area, then the resolution on the right.
        // Docked controls added last sit furthest left.
        var previewFooter = new Panel { Dock = DockStyle.Fill, Height = 24, Margin = new Padding(0) };
        previewFooter.Controls.Add(_resolutionLabel);
        previewFooter.Controls.Add(_bleedCheck);
        previewFooter.Controls.Add(_selectionNav);

        // Print options
        _optionsSection = new CollapsibleSection("Print options") { Collapsed = _settings.PrintOptionsCollapsed };
        _optionsSection.CollapsedChanged += (_, _) => { _settings.PrintOptionsCollapsed = _optionsSection.Collapsed; _settings.Save(); };
        var opt = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        opt.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        opt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _overcoatCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _overcoatCombo.Items.AddRange(OvercoatOptions);
        _overcoatCombo.SelectedIndexChanged += (_, _) =>
        {
            _preview.OvercoatIndex = _overcoatCombo.SelectedIndex;
            _densityUpDown.Enabled = _overcoatCombo.SelectedIndex == 0;
            SaveControlsToSettings();
        };
        _toolTip.SetToolTip(_overcoatCombo,
            "Clear Overcoat Finish (OP Pass):\n" +
            "• Glossy (no pattern): 100% full glossy protective finish over the entire card surface.\n" +
            "• Pattern 1 (kmat_01): SOUND VOLTEX Crest + Laurel Wreath watermark (Portrait).\n" +
            "• Pattern 2 (kmat_02): SOUND VOLTEX Crest in geometric diamond watermark.\n" +
            "• Pattern 3 (kmat_03): SOUND VOLTEX Crest emblem watermark.\n" +
            "• Pattern 4 (kmat_04): Card frame & corner brackets finish.\n" +
            "• Pattern 5 (kmat_05): PUR Genesis Card radial burst & energy beam watermark.\n" +
            "• Pattern 6 (kmat_06): Rotated SOUND VOLTEX Crest + Laurel Wreath (for Landscape cards).");

        _densityUpDown = new NumericUpDown { Minimum = -7, Maximum = 30, Dock = DockStyle.Left, Width = 70 };
        _densityUpDown.ValueChanged += (_, _) => SaveControlsToSettings();
        _toolTip.SetToolTip(_densityUpDown,
            "Thermal Head Print Density (-7 to +30):\n" +
            "Controls the thermal print head energy / print darkness for glossy prints:\n" +
            "• -7 to -1: Lighter / lower thermal energy (reduces heat and prevents bleed on dark art)\n" +
            "• 0: Default SDVX arcade density (standard nominal color balance)\n" +
            "• +1 to +10: Darker, richer color saturation for vibrant character illustrations\n" +
            "• +11 to +30: Maximum thermal energy (deepest black levels and contrast)\n" +
            "\n*Note: Density adjustment is only enabled for full glossy prints (the printer uses this\n" +
            "register to select the KMAT watermark pattern code when an overcoat pattern is active).");

        _preheatCheck = new CheckBox { Text = "Pre-heat thermal head before printing", AutoSize = true, Dock = DockStyle.Fill };
        _preheatCheck.CheckedChanged += (_, _) => SaveControlsToSettings();
        _toolTip.SetToolTip(_preheatCheck,
            "Pre-heat Thermal Head (ESC EWU):\n" +
            "Sends CPU9PreHeat before submitting the print job to warm the thermal head up to optimal operating temperature.\n" +
            "Recommended when printing the first card from a cold start to ensure rich, uniform color density.");

        _offsetUpDown = new NumericUpDown { Minimum = -40, Maximum = 40, Dock = DockStyle.Left, Width = 70 };
        _offsetUpDown.ValueChanged += (_, _) => SaveControlsToSettings();
        _toolTip.SetToolTip(_offsetUpDown,
            "Vertical print offset (pixels, about 0.08 mm each):\n" +
            "Moves the card down (positive) or up (negative) within the bleed when printing, to match where\n" +
            "this printer cuts the card. The game has no such setting; use the test card's edge rulers to tune it.\n" +
            "Only affects printing, not the preview. Not changed by \"Reset to default\".");

        AddOptionRow(opt, "Overcoat:", _overcoatCombo);
        AddOptionRow(opt, "Density:", _densityUpDown);
        AddOptionRow(opt, "Print offset:", _offsetUpDown);
        opt.Controls.Add(_preheatCheck);
        opt.SetColumnSpan(_preheatCheck, 2);


        // Card overlay toggles: each re-renders the selected card.
        CheckBox OverlayToggle(string text, string tip)
        {
            var cb = new CheckBox { Text = text, AutoSize = true, Dock = DockStyle.Fill, Checked = true };
            cb.CheckedChanged += (_, _) =>
            {
                ApplyOverlayToggles();
                SaveControlsToSettings();
                if (!_applyingSettings) _ = RenderSelectedAsync(keepCurrent: true);
            };
            _toolTip.SetToolTip(cb, tip);
            opt.Controls.Add(cb);
            opt.SetColumnSpan(cb, 2);
            return cb;
        }
        _frameCheck = OverlayToggle("Draw card frame",
            "Draw the card frame, logo plaque and ILLUST tag over the artwork.\n" +
            "Frames that are part of the artwork itself (e.g. PUR cards) are not affected.");
        _cardTextCheck = OverlayToggle("Print card text",
            "Print the illustrator name, title and serial number.");
        _copyrightCheck = OverlayToggle("Print copyright line",
            "Print the \"©year Konami Amusement\" line at the bottom-right of the card.\n" +
            "Cards whose artwork already contains the copyright are not affected.");

        _defaultsButton = new Button { Text = "Reset to default", AutoSize = true, Anchor = AnchorStyles.Right };
        _defaultsButton.Click += (_, _) => ResetDefaults();
        _toolTip.SetToolTip(_defaultsButton, "Reset print options to the default SDVX arcade settings");
        opt.Controls.Add(_defaultsButton);
        opt.SetColumnSpan(_defaultsButton, 2);
        _optionsSection.Body.Controls.Add(opt);

        _printButton = new Button
        {
            Text = "Print Card",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Margin = new Padding(0, 2, 0, 0),
            Enabled = false,
        };
        _printButton.Click += (_, _) => ShowPrintConfirmation();
        _toolTip.SetToolTip(_printButton,
            "Open the print confirmation to print this card to the CP-T50D.\n" +
            "With several cards selected (Ctrl/Shift+click or the tile checkboxes), prints them all as a queue.\n" +
            "While a queue is printing, click again to stop after the current card.");

        layout.Controls.Add(_preview, 0, 0);
        layout.Controls.Add(previewFooter, 0, 1);
        layout.Controls.Add(_textBox, 0, 2);
        layout.Controls.Add(_optionsSection, 0, 3);
        layout.Controls.Add(_printButton, 0, 4);

        previewTab.Controls.Add(layout);
        tabs.TabPages.Add(previewTab);
        return tabs;
    }

    private static void AddOptionRow(TableLayoutPanel t, string label, Control c)
    {
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 3) });
        c.Margin = new Padding(0, 3, 0, 3);
        t.Controls.Add(c);
    }

    #endregion

    #region Settings

    private bool _applyingSettings;

    private void ApplySettingsToControls()
    {
        _applyingSettings = true;
        _overcoatCombo.SelectedIndex = Math.Clamp(_settings.Overcoat, 0, OvercoatOptions.Length - 1);
        _densityUpDown.Value = Math.Clamp(_settings.Density, -7, 30);
        _preheatCheck.Checked = _settings.Preheat;
        _offsetUpDown.Value = Math.Clamp(_settings.PrintOffsetY, -40, 40);
        _bleedCheck.Checked = _settings.ShowBleed;
        _frameCheck.Checked = !_settings.HideFrame;
        _cardTextCheck.Checked = !_settings.HideCardText;
        _copyrightCheck.Checked = !_settings.HideCopyright;
        ApplyOverlayToggles();
        _preview.ShowBleed = _settings.ShowBleed;
        _densityUpDown.Enabled = _overcoatCombo.SelectedIndex == 0;

        _preview.OvercoatIndex = _overcoatCombo.SelectedIndex;

        _applyingSettings = false;
    }

    private void SaveControlsToSettings()
    {
        if (_applyingSettings) return;
        _settings.Overcoat = _overcoatCombo.SelectedIndex;
        _settings.Density = (int)_densityUpDown.Value;
        _settings.Preheat = _preheatCheck.Checked;
        _settings.PrintOffsetY = (int)_offsetUpDown.Value;
        _settings.ShowBleed = _bleedCheck.Checked;
        _settings.HideCopyright = !_copyrightCheck.Checked;
        _settings.HideFrame = !_frameCheck.Checked;
        _settings.HideCardText = !_cardTextCheck.Checked;
        _settings.Save();
    }

    private void ApplyOverlayToggles()
    {
        CardCompositor.ShowFrame = _frameCheck.Checked;
        CardCompositor.ShowText = _cardTextCheck.Checked;
        CardCompositor.ShowCopyright = _copyrightCheck.Checked;
    }

    private void ResetDefaults()
    {
        bool overlayChanged = _settings.HideFrame || _settings.HideCardText || _settings.HideCopyright;
        _settings.Overcoat = 0;
        _settings.Density = 0;
        _settings.Preheat = false;
        _settings.ShowBleed = false;
        _settings.HideFrame = false;
        _settings.HideCardText = false;
        _settings.HideCopyright = false;
        ApplySettingsToControls();
        _settings.Save();
        if (overlayChanged) _ = RenderSelectedAsync(keepCurrent: true);
        SetStatus("Print options reset to defaults.");
    }

    #endregion

    #region Logging / status

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetStatus(string message, bool log = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(message, log));
            return;
        }
        _statusLabel.Text = message;
        if (log) Log(message);
    }

    private void SetPrinterState(PrinterState state, string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetPrinterState(state, text));
            return;
        }
        _printerLabel.Text = text;
        _printerDot.ForeColor = PrinterStateColor(state);
        _printerDot.ToolTipText = state switch
        {
            PrinterState.Ready => "Printer online",
            PrinterState.Printing => "Printing",
            PrinterState.Cooling => "Thermal head cooling",
            PrinterState.Searching => "Searching for printer",
            PrinterState.Error => "Printer error",
            PrinterState.Standby => "Standby for paper / ink change",
            _ => "Printer offline",
        };
    }

    public static Color PrinterStateColor(PrinterState state) => state switch
    {
        PrinterState.Ready => Color.ForestGreen,
        PrinterState.Searching => Color.Goldenrod,
        PrinterState.Printing => Color.RoyalBlue,
        PrinterState.Cooling => Color.DarkOrange,
        PrinterState.Standby => Color.SlateGray,
        _ => Color.Firebrick,
    };

    #endregion

    #region File menu

    private async void LoadDataFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select the game's \"data\" or \"contents\" folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (_settings.LastCardFolder != null && Directory.Exists(_settings.LastCardFolder))
            dlg.InitialDirectory = _settings.LastCardFolder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string? data = CardLibrary.ResolveDataFolder(dlg.SelectedPath);
        if (data == null)
        {
            MessageBox.Show(this,
                "No graphics\\chara_card folder was found in the selected folder.\n\n" +
                "Select the game's \"data\" folder (or the \"contents\" folder / game root that contains it).",
                "Not a data folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.LastCardFolder = dlg.SelectedPath;
        _settings.Save();

        ClearCards();
        await LoadPathsAsync(new[] { dlg.SelectedPath });
    }

    /// <summary>Loads genesis_card.xml from the game data folder that contains <paramref name="path"/>.</summary>
    private void LoadCatalogFor(string path)
    {
        string? data = CardLibrary.ResolveDataFolder(path);
        if (FindPrinterDataFolder(data ?? path) is { } printerData)
            ApplyPrinterDataFolder(printerData);
        else if (data != null)
            Log($"No ext\\cpusbxpkm_data folder found for {path}; overcoat previews and printing need it.");
        if (data != null)
        {
            CardFrameLibrary.Instance.SetDataFolder(data);
            CardFonts.SetDataFolder(data);
            Log(CardFonts.UsingGameFont
                ? $"Card text font: {CardFonts.CardFace} (from {Path.Combine(data, "graphics", "font")})"
                : $"Card text font {CardFonts.CardFace} not found in the game folder; using an installed fallback.");
        }
        string? xml = data != null ? CardLibrary.CatalogPath(data) : null;
        if (xml == null)
        {
            if (data != null) Log($"genesis_card.xml not found in {Path.Combine(data, "others")}; card frames will use defaults.");
            return;
        }
        if (string.Equals(GenesisCardCatalog.Instance.SourcePath, xml, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var catalog = GenesisCardCatalog.LoadFrom(xml);
            Log($"Loaded {catalog.Count} card definitions from {xml}");
        }
        catch (Exception ex)
        {
            Log($"Failed to read {xml}: {ex.Message}");
        }
    }

    private async void LoadCardFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Load card file",
            Multiselect = true,
            Filter = "All card files (*.ifs;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.ifs;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" +
                     "|IFS archives (*.ifs)|*.ifs" +
                     "|PNG images (*.png)|*.png" +
                     "|JPEG images (*.jpg;*.jpeg)|*.jpg;*.jpeg" +
                     "|Bitmap images (*.bmp)|*.bmp" +
                     "|Other images (*.gif;*.tif;*.tiff)|*.gif;*.tif;*.tiff" +
                     "|All files (*.*)|*.*",
        };
        if (_settings.LastCardFolder != null && Directory.Exists(_settings.LastCardFolder))
            dlg.InitialDirectory = _settings.LastCardFolder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        await LoadPathsAsync(dlg.FileNames);
    }

    private async Task LoadPathsAsync(IEnumerable<string> paths)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var progress = new Progress<string>(m => SetStatus(m, log: m.StartsWith("Failed")));
        var list = paths.ToList();
        foreach (var p in list) LoadCatalogFor(p);
        list = list.Select(p => Directory.Exists(p) ? CardLibrary.ResolveCardFolder(p) : p).ToList();
        UseWaitCursor = true;
        SetStatus("Loading cards...", log: false);

        try
        {
            var packs = await Task.Run(() =>
            {
                var result = new List<CardPack>();
                var files = new List<string>();
                foreach (var p in list)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (Directory.Exists(p)) result.AddRange(CardLibrary.LoadFolder(p, progress, cts.Token));
                    else if (File.Exists(p)) files.Add(p);
                }

                var looseImages = new CardPack { Name = "Loaded images", Source = "" };
                foreach (var f in files)
                {
                    try
                    {
                        if (CardLibrary.IsImageFile(f))
                            looseImages.Add(CardLibrary.FromImageFile(f));
                        else
                        {
                            ((IProgress<string>)progress).Report($"Reading {Path.GetFileName(f)}...");
                            result.Add(CardLibrary.LoadFile(f));
                        }
                    }
                    catch (Exception ex)
                    {
                        ((IProgress<string>)progress).Report($"Failed to load {Path.GetFileName(f)}: {ex.Message}");
                    }
                }
                if (looseImages.Cards.Count > 0) result.Add(looseImages);
                return result;
            }, cts.Token);

            _browser.AddPacks(packs);
            int cards = packs.Sum(p => p.Cards.Count);
            SetStatus($"Loaded {cards} card(s) from {packs.Count} pack(s). Total: {_browser.TotalCards} card(s).");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Loading cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus($"Load failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ClearCards()
    {
        _thumbs.Clear();
        _browser.ClearPacks();
        SetStatus("Card list cleared.", log: false);
    }

    /// <summary>
    /// Looks for the printer's cpusbxpkm_data folder (LUTs, compensation tables and KMAT overlays)
    /// in contents\ext, walking up from <paramref name="path"/> (game root, contents or data folder).
    /// </summary>
    private static string? FindPrinterDataFolder(string path)
    {
        try
        {
            var start = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory;
            for (var dir = start; dir != null; dir = dir.Parent)
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(dir.FullName, "ext", "cpusbxpkm_data"),
                    Path.Combine(dir.FullName, "contents", "ext", "cpusbxpkm_data"),
                    Path.Combine(dir.FullName, "cpusbxpkm_data"),
                })
                {
                    if (Directory.Exists(candidate) &&
                        (Directory.EnumerateFiles(candidate, "*.lut").Any() || Directory.EnumerateFiles(candidate, "kmat_*.raw").Any()))
                        return candidate;
                }
            }
        }
        catch (Exception) { /* unreadable path: treat as not found */ }
        return null;
    }

    /// <summary>Uses <paramref name="dataDir"/> for the overcoat preview and the printer SDK, and remembers it.</summary>
    private void ApplyPrinterDataFolder(string dataDir)
    {
        if (string.Equals(_settings.PrinterDataFolder, dataDir, StringComparison.OrdinalIgnoreCase) &&
            (_printer == null || _printer.Sdk.DataFolder == dataDir))
            return;

        var luts = Directory.GetFiles(dataDir, "*.lut");
        var csvs = Directory.GetFiles(dataDir, "km1_n_*.csv");
        var kmats = Directory.GetFiles(dataDir, "kmat_*.raw");
        Log($"Printer data folder: {dataDir}");
        Log($"  LUTs: {string.Join(", ", luts.Select(Path.GetFileName))}");
        Log($"  Compensation tables: {string.Join(", ", csvs.Select(Path.GetFileName))}");
        Log($"  KMAT overlays: {string.Join(", ", kmats.Select(Path.GetFileName))}");

        _settings.PrinterDataFolder = dataDir;
        _settings.Save();
        OvercoatManager.Instance.SetDataDirectory(dataDir);
        if (_printer != null) _printer.Sdk.DataFolder = dataDir;
        _preview.Invalidate();
    }

    #endregion

    #region Options menu

    private void LoadPrinterSdkFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select the folder containing CPUSBXPKM.dll and MUsbPuIf.dll",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (_settings.PrinterSdkFolder != null && Directory.Exists(_settings.PrinterSdkFolder))
            dlg.InitialDirectory = _settings.PrinterSdkFolder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string[] required = { "CPUSBXPKM.dll", "MUsbPuIf.dll" };
        var missing = required.Where(f => !File.Exists(Path.Combine(dlg.SelectedPath, f))).ToList();
        if (missing.Count > 0)
        {
            Log($"Printer SDK folder {dlg.SelectedPath} is missing: {string.Join(", ", missing)}");
            MessageBox.Show(this, $"The selected folder is missing:\n{string.Join("\n", missing)}",
                "Printer SDK folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.PrinterSdkFolder = dlg.SelectedPath;
        _settings.Save();
        StartPrinter(dlg.SelectedPath, showErrors: true);
    }

    /// <summary>Loads CPUSBXPKM.dll from <paramref name="folder"/> and starts the status poll.</summary>
    private bool StartPrinter(string folder, bool showErrors)
    {
        if (_printer != null && string.Equals(Path.GetFullPath(_printer.Sdk.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"Printer SDK already loaded from {folder}");
            return true;
        }

        try
        {
            string? dataFolder = ResolvePrinterDataFolder(folder);
            if (dataFolder != null && dataFolder.Any(c => c > 127))
                Log("Warning: the printer data folder path contains non-ASCII characters; the printer SDK may not find its data files.");

            var sdk = PrinterNative.Load(folder, dataFolder, Log);
            if (!sdk.DataFolderHooked)
                Log($"Printer SDK loaded from {folder}; data folder from CPUSBXPKM.ini.");
            else if (sdk.DataFolder != null)
                Log($"Printer SDK loaded from {folder}; data folder: {sdk.DataFolder}");
            else
                Log($"Printer SDK loaded from {folder}, but no cpusbxpkm_data folder was found. " +
                    "Printing will fail until the game folder is loaded with File > Load game data folder.");

            _printer = new PrinterService(sdk, Log);
            _printer.StatusChanged += s =>
            {
                SetPrinterState(s.State, s.StatusText);
                UpdateHoloMediaBoost(s);
            };
            SetPrinterState(PrinterState.Searching, "Searching for printer...");
            SetStatus($"Printer SDK loaded: {folder}", log: false);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to load the printer SDK from {folder}: {ex.Message}");
            SetPrinterState(PrinterState.SdkNotLoaded, "Printer SDK failed to load");
            if (showErrors)
                MessageBox.Show(this, $"Could not load the printer SDK:\n\n{ex.Message}", "Printer SDK",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// Mirrors the game: media type replies tagged 'H' (byte 3) are kept, and bit 1 of byte 2 makes
    /// the card artwork 3% brighter. Re-renders the preview when that changes.
    /// </summary>
    private void UpdateHoloMediaBoost(PrinterSnapshot s)
    {
        if (s.RawMedia is not { Length: >= 4 } media || media[3] != 0x48) return;
        bool boost = (media[2] & 0x02) != 0;
        if (boost == CardCompositor.HoloMediaBoost) return;
        CardCompositor.HoloMediaBoost = boost;
        Log(boost ? "Printer media is holo / overcoat: card artwork is printed 3% brighter, as in the game."
                  : "Printer media is not holo: no extra artwork brightness.");
        BeginInvoke(() => _ = RenderSelectedAsync(keepCurrent: true));
    }

    /// <summary>
    /// The folder with KM1_LUT.lut / KM1_N_*.csv / KMAT_*.raw that CPUSBXPKM.dll needs to print:
    /// the one found with the game data folder, else one near the SDK.
    /// </summary>
    private string? ResolvePrinterDataFolder(string sdkFolder)
    {
        if (_settings.PrinterDataFolder != null && Directory.Exists(_settings.PrinterDataFolder))
            return _settings.PrinterDataFolder;

        string? found = (_settings.LastCardFolder != null ? FindPrinterDataFolder(_settings.LastCardFolder) : null)
                        ?? FindPrinterDataFolder(sdkFolder)
                        ?? FindPrinterDataFolder(Path.Combine(sdkFolder, "..", "KFC"));
        if (found != null) ApplyPrinterDataFolder(Path.GetFullPath(found));
        return found == null ? null : Path.GetFullPath(found);
    }

    private void PrintTestCard()
    {
        string testCardPath = Path.Combine(AppContext.BaseDirectory, "contents", "data", "graphics", "chara_card", "gc_test_s.png");
        if (!File.Exists(testCardPath) && _settings.LastCardFolder != null)
            testCardPath = Path.Combine(_settings.LastCardFolder, "gc_test_s.png");

        CardItem testItem;
        if (File.Exists(testCardPath))
        {
            testItem = CardLibrary.FromImageFile(testCardPath);
        }
        else
        {
            testItem = new CardItem
            {
                Name = "Calibration test card",
                Source = "Generated",
                LoadFull = PrintJobBuilder.CreateTestCard,
            };
        }
        new CardPack { Name = "Test Card Pack" }.Add(testItem);
        using var bmp = testItem.LoadFull();
        _ = ShowConfirmationAndPrint(testItem, bmp);
    }

    private void ShowPrinterStatus()
    {
        if (_printer == null)
        {
            if (MessageBox.Show(this, "The printer SDK is not loaded.\n\nSelect the folder containing CPUSBXPKM.dll and MUsbPuIf.dll now?",
                    "Printer status", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                LoadPrinterSdkFolder();
            if (_printer == null) return;
        }

        if (_statusDialog is { IsDisposed: false })
        {
            _statusDialog.Activate();
            return;
        }
        _statusDialog = new PrinterStatusDialog(_printer);
        _statusDialog.FormClosed += (_, _) => _statusDialog = null;
        _statusDialog.Show(this);
        _ = Task.Run(_printer.Refresh);
    }

    private void ShowPrintConfirmation()
    {
        // While a queue runs, the print button cancels it after the current card.
        if (_printing)
        {
            if (_queueTotal > 1 && !_cancelQueue)
            {
                _cancelQueue = true;
                Log("Print queue: cancel requested; stopping after the current card.");
                UpdatePrintButton();
            }
            return;
        }
        var card = _browser.SelectedCard;
        if (card == null || _currentImage == null) return;
        var cards = _browser.SelectedCards;
        _ = ConfirmAndPrintAsync(cards.Count > 1 ? cards : new[] { card }, card, _currentImage);
    }

    private Task ShowConfirmationAndPrint(CardItem card, Image image) => ConfirmAndPrintAsync(new[] { card }, card, image);

    /// <summary>
    /// Shows the confirmation dialog and prints <paramref name="cards"/> one after another (a print queue
    /// when there are several). <paramref name="shownImage"/> is the already-rendered image of
    /// <paramref name="shown"/>; it is only used before the first await, so the caller may dispose it as
    /// soon as this returns. The other cards are rendered when their turn comes.
    /// </summary>
    private async Task ConfirmAndPrintAsync(IReadOnlyList<CardItem> cards, CardItem shown, Image shownImage)
    {
        if (_printing)
        {
            MessageBox.Show(this, "A card is already printing.", "Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_printer == null)
        {
            if (MessageBox.Show(this, "The printer SDK is not loaded.\n\nSelect the folder containing CPUSBXPKM.dll and MUsbPuIf.dll now?",
                    "Print", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                LoadPrinterSdkFolder();
            if (_printer == null) return;
        }

        var settings = new PrintJobSettings(
            OvercoatIndex: _overcoatCombo.SelectedIndex,
            Density: (int)_densityUpDown.Value,
            Preheat: _preheatCheck.Checked,
            OffsetY: (int)_offsetUpDown.Value);

        int total = cards.Count;
        using (var dlg = new PrintConfirmDialog(
            card: shown,
            cardImage: shownImage,
            overcoatIndex: settings.OvercoatIndex,
            overcoatName: OvercoatOptions[settings.OvercoatIndex],
            density: settings.Density,
            preheat: settings.Preheat,
            printerStatus: _printerLabel.Text ?? "Printer offline",
            queue: cards))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                Log("Print job cancelled by user.");
                SetStatus("Print cancelled.", log: false);
                return;
            }
        }

        // The shown card's image may be disposed after the first await, so prepare it now.
        Bitmap? shownPrint;
        try
        {
            shownPrint = PrintJobBuilder.ToPrintBitmap(shownImage, settings.OffsetY);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not prepare {shown.Name} for printing: {ex.Message}");
            return;
        }

        Log($"{(total > 1 ? $"Print queue of {total} cards" : $"Printing {shown.Name}")} " +
            $"(Overcoat: {OvercoatOptions[settings.OvercoatIndex]}, " +
            $"Density: {(settings.OvercoatIndex == 0 ? settings.Density.ToString("+#;-#;0") : "locked")}, " +
            $"Offset: {settings.OffsetY:+#;-#;0} px, Pre-heat: {(settings.Preheat ? "on" : "off")}).");
        _printing = true;
        _cancelQueue = false;
        _queueTotal = total;
        _queuePosition = 0;
        UpdatePrintButton();

        int printed = 0;
        CardItem? current = null;
        try
        {
            for (int i = 0; i < total; i++)
            {
                if (_cancelQueue) break;
                current = cards[i];
                _queuePosition = i + 1;
                UpdatePrintButton();
                string prefix = total > 1 ? $"Card {i + 1}/{total} ({current.Name}): " : "";

                Bitmap printBitmap;
                if (current == shown && shownPrint != null)
                {
                    printBitmap = shownPrint;
                    shownPrint = null;
                }
                else
                {
                    SetStatus(prefix + "preparing...", log: false);
                    using var full = await Task.Run(current.LoadFull);
                    printBitmap = PrintJobBuilder.ToPrintBitmap(full, settings.OffsetY);
                }

                using (printBitmap)
                {
                    if (total > 1) Log($"Print queue {i + 1}/{total}: {current.Name}");
                    var progress = new Progress<string>(m => SetStatus(prefix + m, log: false));
                    await _printer.PrintAsync(printBitmap, settings, progress);
                }
                printed++;
            }

            if (_cancelQueue && printed < total)
                SetStatus($"Print queue cancelled: printed {printed} of {total} cards.");
            else
                SetStatus(total > 1 ? $"Printed all {total} cards." : $"Printed {shown.Name}.");
        }
        catch (Exception ex)
        {
            string where = total > 1 ? $"Printed {printed} of {total} cards; stopped at {current?.Name}.\n\n" : "";
            SetStatus($"Print failed{(current != null && total > 1 ? $" on {current.Name}" : "")}: {ex.Message}");
            bool media = ex is PrinterException { StatusCode: PrinterStatusCodes.MediaEmpty };
            MessageBox.Show(this,
                where + (media ? $"{ex.Message}\n\nReplace the ribbon / card stock and try again." : ex.Message),
                "Print failed", MessageBoxButtons.OK, media ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
        }
        finally
        {
            shownPrint?.Dispose();
            _printing = false;
            _cancelQueue = false;
            _queueTotal = 0;
            UpdatePrintButton();
        }
    }

    private void ExportSelectedPng()
    {
        var card = _browser.SelectedCard;
        if (card == null) return;
        if (_currentImage == null)
        {
            MessageBox.Show(this, "The selected card is still loading.", "Export PNG", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export selected card as PNG",
            Filter = "PNG image (*.png)|*.png",
            FileName = Path.GetFileNameWithoutExtension(card.Name) + ".png",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _currentImage.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
            SetStatus($"Exported {card.Name} to {dlg.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Export failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion

    #region Selection / preview

    private void OnSelectedCardChanged()
    {
        var card = _browser.SelectedCard;
        _textTimer.Stop();
        LoadCardText(card);
        UpdateSelectionNav();
        _ = RenderSelectedAsync(keepCurrent: false);
    }

    /// <summary>Shows ◀ 1/N ▶ under the preview while several cards are selected.</summary>
    private void UpdateSelectionNav()
    {
        var selection = _browser.SelectedCards;
        bool multi = selection.Count > 1;
        _selectionNav.Visible = multi;
        if (multi)
        {
            int i = _browser.SelectedCard is { } c ? IndexOf(selection, c) : -1;
            _selectionCounter.Text = $"{i + 1}/{selection.Count}";
        }
        UpdatePrintButton();
    }

    private static int IndexOf(IReadOnlyList<CardItem> list, CardItem card)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == card) return i;
        return -1;
    }

    /// <summary>Previews the previous / next selected card (wrapping around).</summary>
    private void StepSelection(int delta)
    {
        var selection = _browser.SelectedCards;
        if (selection.Count < 2) return;
        int i = _browser.SelectedCard is { } c ? IndexOf(selection, c) : -1;
        int next = ((i < 0 ? 0 : i + delta) % selection.Count + selection.Count) % selection.Count;
        _browser.FocusCard(selection[next]);
    }

    /// <summary>
    /// "Print Card" / "Print N Cards"; while a queue is printing it becomes "Cancel Queue (i/N)" and stops
    /// the queue after the current card.
    /// </summary>
    private void UpdatePrintButton()
    {
        if (_printing)
        {
            if (_queueTotal > 1)
            {
                _printButton.Text = _cancelQueue ? "Stopping after this card..." : $"Cancel Queue ({_queuePosition}/{_queueTotal})";
                _printButton.Enabled = !_cancelQueue;
            }
            else
            {
                _printButton.Text = "Printing...";
                _printButton.Enabled = false;
            }
            return;
        }
        int count = _browser.SelectedCards.Count;
        _printButton.Text = count > 1 ? $"Print {count} Cards" : "Print Card";
        _printButton.Enabled = _currentImage != null;
    }

    /// <summary>Renders the selected card into the preview. keepCurrent keeps the old image visible while re-rendering.</summary>
    private async Task RenderSelectedAsync(bool keepCurrent)
    {
        var card = _browser.SelectedCard;
        int version = ++_selectionVersion;

        if (!keepCurrent)
        {
            _preview.Image = null;
            _currentImage?.Dispose();
            _currentImage = null;
            _miExportPng.Enabled = false;
            UpdatePrintButton();
        }

        if (card == null)
        {
            _preview.Placeholder = "Preview\nnot\navailable";
            _resolutionLabel.Text = "No card selected";
            return;
        }

        if (!keepCurrent)
        {
            _preview.Placeholder = "Loading...";
            _resolutionLabel.Text = card.Name;
        }

        try
        {
            var bmp = await Task.Run(card.LoadFull);
            if (version != _selectionVersion)
            {
                bmp.Dispose();
                return;
            }
            var old = _currentImage;
            _currentImage = bmp;
            _preview.Image = bmp;
            old?.Dispose();
            _resolutionLabel.Text = $"{bmp.Width} x {bmp.Height} px";
            _miExportPng.Enabled = true;
            UpdatePrintButton();
            if (!keepCurrent) SetStatus($"{card.Name}  ({card.Source})", log: false);
        }
        catch (Exception ex)
        {
            if (version != _selectionVersion) return;
            _preview.Placeholder = "Preview\nnot\navailable";
            _resolutionLabel.Text = "Failed to load";
            SetStatus($"Failed to load {card.Name}: {ex.Message}");
        }
    }

    /// <summary>Fills the card text boxes with the selected card's text (an edit, or the catalog default).</summary>
    private void LoadCardText(CardItem? card)
    {
        _loadingText = true;
        try
        {
            var info = card != null ? GenesisCardCatalog.Instance.Lookup(card.Name) : null;
            // Every catalog card gets its serial printed; the title / illustrator only where the game prints them.
            bool editable = info is { FromCatalog: true };
            _textBox.Body.Enabled = editable;
            _toolTip.SetToolTip(_textBox.Body, editable || card == null ? "" :
                "This image is not in genesis_card.xml, so no card text is printed on it.");

            var text = editable ? CardTextOverrides.Get(card!.Name) : default;
            _illustratorText.Text = text.Illustrator ?? "";
            _titleText.Text = text.Title ?? "";
            _serialText.Text = text.Serial ?? "";
            _titleText.Enabled = info?.ShowsTitle == true;
            _illustratorText.Enabled = info?.ShowsIllustrator == true;
            _resetTextButton.Enabled = editable && CardTextOverrides.HasOverride(card!.Name);
        }
        finally
        {
            _loadingText = false;
        }
    }

    private void ApplyCardText()
    {
        var card = _browser.SelectedCard;
        if (card == null || !_textBox.Body.Enabled) return;
        CardTextOverrides.Set(card.Name, new CardText(_illustratorText.Text, _titleText.Text, _serialText.Text));
        _resetTextButton.Enabled = CardTextOverrides.HasOverride(card.Name);
        _ = RenderSelectedAsync(keepCurrent: true);
    }

    private void ResetCardText()
    {
        var card = _browser.SelectedCard;
        if (card == null) return;
        _textTimer.Stop();
        CardTextOverrides.Reset(card.Name);
        LoadCardText(card);
        _ = RenderSelectedAsync(keepCurrent: true);
    }

    #endregion

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _repaintTimer.Stop();
        _textTimer.Stop();
        _loadCts?.Cancel();
        _thumbs.Dispose();
        _printer?.Dispose();
        _currentImage?.Dispose();
        base.OnFormClosed(e);
    }
}
