using System.Drawing.Drawing2D;

namespace CPT50DTools.Controls;

/// <summary>
/// Shared look for collapsible headers: the card browser's pack headers and the
/// Card text / Print options sections all draw through <see cref="Draw"/>.
/// </summary>
public static class SectionHeader
{
    public const int Height = 26;

    public static void Draw(Graphics g, Rectangle r, string title, bool collapsed, Font bold, Font normal,
        string? rightText = null, bool hot = false, bool focused = false)
    {
        var top = hot ? Color.FromArgb(240, 247, 255) : Color.FromArgb(250, 250, 250);
        var bottom = hot ? Color.FromArgb(220, 235, 250) : Color.FromArgb(232, 232, 232);
        using (var bg = new LinearGradientBrush(r, top, bottom, 90f))
            g.FillRectangle(bg, r);
        using (var pen = new Pen(Color.FromArgb(205, 205, 205)))
            g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);

        // Expand/collapse chevron
        var c = new Point(r.Left + 12, r.Top + r.Height / 2);
        Point[] tri = collapsed
            ? new[] { new Point(c.X - 2, c.Y - 5), new Point(c.X + 4, c.Y), new Point(c.X - 2, c.Y + 5) }
            : new[] { new Point(c.X - 5, c.Y - 2), new Point(c.X + 5, c.Y - 2), new Point(c.X, c.Y + 4) };
        var oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Color.FromArgb(80, 80, 80))) g.FillPolygon(b, tri);
        g.SmoothingMode = oldSmoothing;

        using (var brush = new SolidBrush(Color.FromArgb(30, 30, 30)))
        using (var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        })
        {
            g.DrawString(title, bold, brush, new Rectangle(r.Left + 24, r.Top, r.Width - 30, r.Height), sf);
        }

        if (!string.IsNullOrEmpty(rightText))
        {
            using var countBrush = new SolidBrush(SystemColors.GrayText);
            using var sfRight = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            g.DrawString(rightText, normal, countBrush, new Rectangle(r.Left, r.Top, r.Width - 10, r.Height), sfRight);
        }

        if (focused)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(r, -2, -2));
    }
}
