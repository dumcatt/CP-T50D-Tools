using System.Reflection;
using CPT50DTools.Cards;
using CPT50DTools.Controls;

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

    private static readonly string[] FitModeOptions =
    {
        "Crop to fit (fill card)",
        "Fit inside (letterbox)",
        "Stretch",
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
    private ComboBox _overcoatCombo = null!;
    private NumericUpDown _densityUpDown = null!;
    private ComboBox _fitCombo = null!;
    private CheckBox _preheatCheck = null!;
    private Button _defaultsButton = null!;
    private Button _printButton = null!;

    // Status bar
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _printerDot = null!;
    private ToolStripStatusLabel _printerLabel = null!;

    private readonly System.Windows.Forms.Timer _repaintTimer = new() { Interval = 100 };
    private volatile bool _thumbsDirty;

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
        if (_settings.LutOverlayFolder != null)
            OvercoatManager.Instance.SetDataDirectory(_settings.LutOverlayFolder);

        _thumbs.ThumbnailLoaded += _ => _thumbsDirty = true;
        _repaintTimer.Tick += (_, _) =>
        {
            if (!_thumbsDirty) return;
            _thumbsDirty = false;
            _browser.Invalidate();
        };
        _repaintTimer.Start();

        SetPrinterState(online: false, "Printer SDK not loaded");
        Log($"CP-T50D Tools {_version} started.");
        if (_settings.PrinterSdkFolder != null) Log($"Printer SDK folder: {_settings.PrinterSdkFolder}");
        if (_settings.LutOverlayFolder != null) Log($"LUT & overlay folder: {_settings.LutOverlayFolder}");

        if (startupPaths is { Length: > 0 })
        {
            Shown += async (_, _) => await LoadPathsAsync(startupPaths.Select(p =>
                Directory.Exists(p) ? CardLibrary.ResolveCardFolder(p) : p));
        }
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
        file.DropDownItems.Add(new ToolStripMenuItem("Load card &folder...", null, (_, _) => LoadCardFolder(), Keys.Control | Keys.Shift | Keys.O));
        file.DropDownItems.Add(new ToolStripMenuItem("Load card f&ile...", null, (_, _) => LoadCardFile(), Keys.Control | Keys.O));
        file.DropDownItems.Add(new ToolStripMenuItem("Load &LUT && Overlay folder...", null, (_, _) => LoadLutOverlayFolder()));
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

        _searchBox = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search cards or packs", BorderStyle = BorderStyle.FixedSingle };
        _searchBox.TextChanged += (_, _) => _browser.Filter = _searchBox.Text.Trim();
        _toolTip.SetToolTip(_searchBox, "Type to filter cards and packs by name");

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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // options
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // defaults button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // print button

        _preview = new PreviewBox { Dock = DockStyle.Fill, Margin = new Padding(0) };

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

        // Print options
        var optionsBox = new GroupBox
        {
            Text = "Print options",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 4, 6, 6),
        };
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

        _fitCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _fitCombo.Items.AddRange(FitModeOptions);
        _fitCombo.SelectedIndexChanged += (_, _) => SaveControlsToSettings();
        _toolTip.SetToolTip(_fitCombo,
            "Image Scaling & Fit Mode (Printable Card Area: 712x1072):\n" +
            "• Crop to fit (fill card): Scales image to fill entire card, cleanly trimming outer registration marks.\n" +
            "• Fit inside (letterbox): Preserves original aspect ratio without cropping, fitting inside card boundaries.\n" +
            "• Stretch: Stretches image directly to 712x1072.");

        _preheatCheck = new CheckBox { Text = "Pre-heat thermal head before printing", AutoSize = true, Dock = DockStyle.Fill };
        _preheatCheck.CheckedChanged += (_, _) => SaveControlsToSettings();
        _toolTip.SetToolTip(_preheatCheck,
            "Pre-heat Thermal Head (ESC EWU):\n" +
            "Sends CPU9PreHeat before submitting the print job to warm the thermal head up to optimal operating temperature.\n" +
            "Recommended when printing the first card from a cold start to ensure rich, uniform color density.");

        AddOptionRow(opt, "Overcoat:", _overcoatCombo);
        AddOptionRow(opt, "Density:", _densityUpDown);
        AddOptionRow(opt, "Image fit:", _fitCombo);
        opt.Controls.Add(_preheatCheck);
        opt.SetColumnSpan(_preheatCheck, 2);
        optionsBox.Controls.Add(opt);

        _defaultsButton = new Button { Text = "Default settings", Dock = DockStyle.Fill, Height = 28, Margin = new Padding(0, 4, 0, 2) };
        _defaultsButton.Click += (_, _) => ResetDefaults();
        _toolTip.SetToolTip(_defaultsButton, "Reset print options to default SDVX arcade settings");

        _printButton = new Button
        {
            Text = "Print Card",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Margin = new Padding(0, 2, 0, 0),
            Enabled = false,
        };
        _printButton.Click += (_, _) => ShowPrintConfirmation();
        _toolTip.SetToolTip(_printButton, "Open print preview and confirmation dialog to print this card to the CP-T50D");

        layout.Controls.Add(_preview, 0, 0);
        layout.Controls.Add(_resolutionLabel, 0, 1);
        layout.Controls.Add(optionsBox, 0, 2);
        layout.Controls.Add(_defaultsButton, 0, 3);
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
        _fitCombo.SelectedIndex = Math.Clamp(_settings.FitMode, 0, FitModeOptions.Length - 1);
        _preheatCheck.Checked = _settings.Preheat;
        _densityUpDown.Enabled = _overcoatCombo.SelectedIndex == 0;

        _preview.OvercoatIndex = _overcoatCombo.SelectedIndex;

        _applyingSettings = false;
    }

    private void SaveControlsToSettings()
    {
        if (_applyingSettings) return;
        _settings.Overcoat = _overcoatCombo.SelectedIndex;
        _settings.Density = (int)_densityUpDown.Value;
        _settings.FitMode = _fitCombo.SelectedIndex;
        _settings.Preheat = _preheatCheck.Checked;
        _settings.Save();
    }

    private void ResetDefaults()
    {
        _settings.Overcoat = 0;
        _settings.Density = 0;
        _settings.FitMode = 0;
        _settings.Preheat = false;
        ApplySettingsToControls();
        _settings.Save();
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

    private void SetPrinterState(bool online, string text)
    {
        _printerLabel.Text = text;
        _printerDot.ForeColor = online ? Color.ForestGreen : Color.Firebrick;
        _printerDot.ToolTipText = online ? "Printer online" : "Printer offline";
    }

    #endregion

    #region File menu

    private async void LoadCardFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select a card folder (containing .ifs packs and/or card images)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (_settings.LastCardFolder != null && Directory.Exists(_settings.LastCardFolder))
            dlg.InitialDirectory = _settings.LastCardFolder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _settings.LastCardFolder = dlg.SelectedPath;
        _settings.Save();

        string folder = CardLibrary.ResolveCardFolder(dlg.SelectedPath);

        if (!string.Equals(folder, dlg.SelectedPath, StringComparison.OrdinalIgnoreCase))
            Log($"Using card folder {folder}");

        ClearCards();
        await LoadPathsAsync(new[] { folder });
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

    private void LoadLutOverlayFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select the \"ext\" folder containing the LUTs and KMAT overlays",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (_settings.LutOverlayFolder != null && Directory.Exists(_settings.LutOverlayFolder))
            dlg.InitialDirectory = _settings.LutOverlayFolder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string dataDir = new[]
        {
            Path.Combine(dlg.SelectedPath, "cpusbxpkm_data"),
            Path.Combine(dlg.SelectedPath, "ext", "cpusbxpkm_data"),
            dlg.SelectedPath,
        }.First(Directory.Exists);

        var luts = Directory.GetFiles(dataDir, "*.lut");
        var csvs = Directory.GetFiles(dataDir, "km1_n_*.csv");
        var kmats = Directory.GetFiles(dataDir, "kmat_*.raw");

        Log($"LUT & overlay folder: {dataDir}");
        Log($"  LUTs: {string.Join(", ", luts.Select(Path.GetFileName))}");
        Log($"  Compensation tables: {string.Join(", ", csvs.Select(Path.GetFileName))}");
        Log($"  KMAT overlays: {string.Join(", ", kmats.Select(Path.GetFileName))}");

        if (luts.Length == 0 && kmats.Length == 0)
        {
            MessageBox.Show(this, $"No LUT (*.lut) or KMAT overlay (kmat_*.raw) files were found in:\n{dataDir}",
                "LUT & Overlay folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus("No LUT / overlay files found.", log: false);
            return;
        }

        _settings.LutOverlayFolder = dataDir;
        _settings.Save();
        OvercoatManager.Instance.SetDataDirectory(dataDir);
        _preview.Invalidate();
        SetStatus($"Loaded LUT & overlay folder ({luts.Length} LUT, {kmats.Length} overlay(s)).");
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
        SetPrinterState(online: false, "SDK loaded - printer not connected");
        SetStatus($"Printer SDK folder set: {dlg.SelectedPath}");
    }

    private void PrintTestCard()
    {
        string testCardPath = Path.Combine(AppContext.BaseDirectory, "contents", "data", "graphics", "chara_card", "gc_test_s.png");
        if (!File.Exists(testCardPath) && _settings.LastCardFolder != null)
            testCardPath = Path.Combine(_settings.LastCardFolder, "gc_test_s.png");

        if (File.Exists(testCardPath))
        {
            var testItem = CardLibrary.FromImageFile(testCardPath);
            testItem.Pack = new CardPack { Name = "Test Card Pack" };
            using var bmp = testItem.LoadFull();
            ShowConfirmationAndPrint(testItem, bmp);
        }
        else
        {
            NotImplementedYet("Print test card");
        }
    }

    private void ShowPrinterStatus() => NotImplementedYet("Printer status");

    private void ShowPrintConfirmation()
    {
        var card = _browser.SelectedCard;
        if (card == null || _currentImage == null) return;
        ShowConfirmationAndPrint(card, _currentImage);
    }

    private void ShowConfirmationAndPrint(CardItem card, Image image)
    {
        using var dlg = new PrintConfirmDialog(
            card: card,
            cardImage: image,
            overcoatIndex: _overcoatCombo.SelectedIndex,
            overcoatName: OvercoatOptions[_overcoatCombo.SelectedIndex],
            density: (int)_densityUpDown.Value,
            fitMode: _fitCombo.SelectedIndex,
            preheat: _preheatCheck.Checked,
            printerStatus: _printerLabel.Text ?? "Printer offline");

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            Log($"Print confirmed for {card.Name} (Overcoat: {OvercoatOptions[_overcoatCombo.SelectedIndex]}, Density: {_densityUpDown.Value}).");
            MessageBox.Show(this,
                $"Print job for '{card.Name}' confirmed.\n\n" +
                $"Finish: {OvercoatOptions[_overcoatCombo.SelectedIndex]}\n\n" +
                "Printer hardware transmission functions will be implemented in the next step.",
                "Print Job Submitted", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            Log("Print job cancelled by user.");
            SetStatus("Print cancelled.", log: false);
        }
    }

    private void NotImplementedYet(string feature)
    {
        Log($"{feature}: printer functions are not implemented yet.");
        MessageBox.Show(this, $"{feature} is not implemented yet.\nPrinter functions will be added in the next step.",
            "Not implemented", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private async void OnSelectedCardChanged()
    {
        var card = _browser.SelectedCard;
        int version = ++_selectionVersion;

        _preview.Image = null;
        _currentImage?.Dispose();
        _currentImage = null;
        _miExportPng.Enabled = false;
        _printButton.Enabled = false;

        if (card == null)
        {
            _preview.Placeholder = "Preview\nnot\navailable";
            _resolutionLabel.Text = "No card selected";
            return;
        }

        _preview.Placeholder = "Loading...";
        _resolutionLabel.Text = card.Name;

        try
        {
            var bmp = await Task.Run(card.LoadFull);
            if (version != _selectionVersion)
            {
                bmp.Dispose();
                return;
            }
            _currentImage = bmp;
            _preview.Image = bmp;
            _resolutionLabel.Text = $"{bmp.Width} x {bmp.Height} px";
            _miExportPng.Enabled = true;
            _printButton.Enabled = true;
            SetStatus($"{card.Name}  ({card.Source})", log: false);
        }
        catch (Exception ex)
        {
            if (version != _selectionVersion) return;
            _preview.Placeholder = "Preview\nnot\navailable";
            _resolutionLabel.Text = "Failed to load";
            SetStatus($"Failed to load {card.Name}: {ex.Message}");
        }
    }

    #endregion

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _repaintTimer.Stop();
        _loadCts?.Cancel();
        _thumbs.Dispose();
        _currentImage?.Dispose();
        base.OnFormClosed(e);
    }
}
