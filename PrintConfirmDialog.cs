using CPT50DTools.Cards;
using CPT50DTools.Controls;

namespace CPT50DTools;

public sealed class PrintConfirmDialog : Form
{
    public PrintConfirmDialog(
        CardItem card,
        Image cardImage,
        int overcoatIndex,
        string overcoatName,
        int density,
        int fitMode,
        bool preheat,
        string printerStatus)
    {
        Text = "Confirm Print - Mitsubishi CP-T50D";
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
        var preview = new PreviewBox
        {
            Dock = DockStyle.Fill,
            Image = cardImage,
            OvercoatIndex = overcoatIndex,
            // The printer receives the full image, bleed included; the dashed line marks the trim.
            ShowBleed = true,
        };
        previewGroup.Controls.Add(preview);
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
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            AutoSize = true,
        };
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, string val, bool bold = false)
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
        }

        AddRow("Card:", card.Name, bold: true);
        AddRow("Source:", Path.GetFileName(card.Pack.Name));
        AddRow("Resolution:", $"{cardImage.Width} x {cardImage.Height} px");
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

        var continueBtn = DialogButton("Continue", DialogResult.OK, primary: true);
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
    }
}
