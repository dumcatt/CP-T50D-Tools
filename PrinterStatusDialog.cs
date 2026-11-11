using CPT50DTools.Printer;

namespace CPT50DTools;

/// <summary>Modeless window with live printer telemetry, updated from the status poll.</summary>
public sealed class PrinterStatusDialog : Form
{
    private readonly PrinterService _service;
    private readonly Dictionary<string, Label> _values = new();
    private readonly Button _preheatButton;
    private readonly Button _refreshButton;
    private readonly Button _standbyButton;

    public PrinterStatusDialog(PrinterService service)
    {
        _service = service;
        Text = "Printer Status - Mitsubishi CP-T50D";
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(540, 420);
        MinimumSize = new Size(540, 380);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var group = new GroupBox { Text = "Printer", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var name in new[] { "Status", "Model", "USB port", "Serial", "Firmware", "Media", "Cards remaining", "Head temperature", "Lifetime prints", "SDK folder", "Data folder", "Last update" })
        {
            details.Controls.Add(new Label
            {
                Text = name + ":",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = SystemColors.ControlDarkDark,
                Margin = new Padding(0, 5, 4, 5),
            });
            var val = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 0, 5), Text = "-" };
            _values[name] = val;
            details.Controls.Add(val);
        }
        _values["Status"].Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        group.Controls.Add(details);
        layout.Controls.Add(group, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(3, 8, 3, 0),
        };
        Button MakeButton(string text) => new()
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(100, 30),
            Margin = new Padding(8, 0, 0, 0),
        };
        var close = MakeButton("Close");
        close.Click += (_, _) => Close();
        _refreshButton = MakeButton("Refresh");
        _refreshButton.Click += async (_, _) =>
        {
            _refreshButton.Enabled = false;
            try { await Task.Run(_service.Refresh); }
            finally { _refreshButton.Enabled = true; }
        };
        _preheatButton = MakeButton("Pre-heat head");
        _preheatButton.Click += async (_, _) =>
        {
            _preheatButton.Enabled = false;
            uint ret = await Task.Run(_service.PreHeat);
            _preheatButton.Enabled = true;
            if (ret != 0)
                MessageBox.Show(this, $"Pre-heat failed: {PrinterStatusCodes.Format(ret)}", "Pre-heat",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        _standbyButton = MakeButton("Paper/Ink change");
        _standbyButton.Click += (_, _) => ToggleStandby();
        buttons.Controls.AddRange(new Control[] { close, _refreshButton, _preheatButton, _standbyButton });
        layout.Controls.Add(buttons, 0, 1);

        CancelButton = close;
        Controls.Add(layout);

        _service.StatusChanged += OnStatusChanged;
        Display(_service.Snapshot);
    }

    private void OnStatusChanged(PrinterSnapshot s)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => Display(s)); } catch (InvalidOperationException) { }
    }

    private void Display(PrinterSnapshot s)
    {
        if (IsDisposed) return;
        var p = s.Printer;
        _values["Status"].Text = s.StatusText;
        _values["Status"].ForeColor = MainForm.PrinterStateColor(s.State);
        _values["Model"].Text = p is { } m ? $"{m.ModelName} (0x{m.ModelId:X4}, sub-model 0x{m.SubModel:X4})" : "-";
        _values["USB port"].Text = p is { } port ? port.PortNum.ToString() : "-";
        _values["Serial"].Text = p is { } ser && ser.SerialString.Length > 0 ? ser.SerialString : "-";
        _values["Firmware"].Text = s.Firmware ?? "-";
        _values["Media"].Text = s.RawMedia is { } media
            ? $"{(s.HoloMedia == true ? "Holo / overcoat" : "Standard")} ({Convert.ToHexString(media)})"
            : "-";
        _values["Cards remaining"].Text = s.PaperRemain?.ToString() ?? "-";
        _values["Head temperature"].Text = s.HeadTemp?.ToString() ?? "-";
        _values["Lifetime prints"].Text = s.TotalPrints?.ToString("N0") ?? "-";
        _values["SDK folder"].Text = _service.Sdk.Folder;
        _values["Data folder"].Text = _service.Sdk.DataFolder == null ? "(built-in tables / CPUSBXPKM.ini)"
            : _service.Sdk.DataFolderHooked ? _service.Sdk.DataFolder : "(from CPUSBXPKM.ini)";
        _values["Last update"].Text = _service.IsStandby ? "Paused for paper / ink change"
            : _service.IsPrinting ? "Paused while printing" : s.Time.ToString("HH:mm:ss");
        _preheatButton.Enabled = s.Printer != null && !_service.IsPrinting && !_service.IsStandby;
        _refreshButton.Enabled = !_service.IsStandby;
        _standbyButton.Text = _service.IsStandby ? "Resume printer" : "Paper/Ink change";
        _standbyButton.Enabled = !_service.IsPrinting;
    }

    /// <summary>Pauses printer communication for a paper / ink change, or resumes and rescans the printer.</summary>
    private void ToggleStandby()
    {
        try
        {
            bool entering = !_service.IsStandby;
            _service.SetStandby(entering);
            if (entering)
                MessageBox.Show(this,
                    "The printer is in standby and the app has stopped talking to it.\n\n" +
                    "You can now open the printer door and replace the ink ribbon and card stock.\n" +
                    "Close the door, then click \"Resume printer\" to reconnect and read the new card count.",
                    "Paper/Ink change", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Paper/Ink change", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Display(_service.Snapshot);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _service.StatusChanged -= OnStatusChanged;
        base.OnFormClosed(e);
    }
}
