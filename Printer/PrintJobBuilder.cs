using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CPT50DTools.Cards;

namespace CPT50DTools.Printer;

/// <summary>Settings for one print job, taken from the Print options panel.</summary>
/// <param name="OffsetY">Pixels to move the card down (towards its bottom edge) on the print, to compensate
/// for where this printer cuts the card. The game has no such setting; 0 prints exactly as it does.</param>
public sealed record PrintJobSettings(int OvercoatIndex, int Density, bool Preheat, int OffsetY = 0);

/// <summary>Converts card images into the 1072x712 BGR buffer and structs CPUASendImagePrint expects.</summary>
public static class PrintJobBuilder
{
    public const int BufferSize = PrinterNative.RowStride * PrinterNative.PrintHeight;

    /// <summary>
    /// Rotates a portrait card to the printer's landscape orientation and fills 1072x712 with it,
    /// centred and cropped like the game (images of another size are scaled to cover the card first).
    /// Portrait -> landscape is a 90° counter-clockwise turn (as print_image.py does). The card face
    /// is not centred in the art (7 px bleed on the left, 41 px on the right), so turning the other
    /// way puts the wide bleed on the wrong side and the printer trims into the art's left edge.
    /// <paramref name="offsetY"/> moves the card down into the bottom bleed; the card's top is the print's
    /// left edge, so that is a shift to the right here, with black filling the gap.
    /// </summary>
    public static Bitmap ToPrintBitmap(Image source, int offsetY = 0)
    {
        const int W = PrinterNative.PrintWidth, H = PrinterNative.PrintHeight;
        using var rotated = new Bitmap(source);
        if (rotated.Height > rotated.Width) rotated.RotateFlip(RotateFlipType.Rotate270FlipNone);

        var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        // Black like the game's card render target: transparent art margins and the offset gap print black.
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        double s = Math.Max((double)W / rotated.Width, (double)H / rotated.Height);
        // 714x1074 card art is 2 px larger than the print area; the game crops it 1:1 rather than
        // resampling, so do the same whenever the scale is within 1% of 1 to keep the print sharp.
        if (Math.Abs(s - 1) < 0.01) s = 1;
        float w = (float)(rotated.Width * s), h = (float)(rotated.Height * s);
        var dest = new RectangleF((W - w) / 2f + offsetY, (H - h) / 2f, w, h);

        // TileFlipXY avoids the semi-transparent fringe GDI+ adds at the image edges when scaling.
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(rotated,
            new[] { dest.Location, new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom) },
            new RectangleF(0, 0, rotated.Width, rotated.Height), GraphicsUnit.Pixel, attrs);
        return bmp;
    }

    /// <summary>
    /// Copies a 1072x712 bitmap into unmanaged memory as 24-bit BGR in bottom-up row order
    /// (positive row stride, as SOUND VOLTEX sends it). Free with Marshal.FreeHGlobal.
    /// </summary>
    public static IntPtr ToBgrBottomUp(Bitmap bmp)
    {
        if (bmp.Width != PrinterNative.PrintWidth || bmp.Height != PrinterNative.PrintHeight)
            throw new ArgumentException($"Print bitmap must be {PrinterNative.PrintWidth}x{PrinterNative.PrintHeight}.");

        var buffer = Marshal.AllocHGlobal(BufferSize);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[PrinterNative.RowStride];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                Marshal.Copy(row, 0, buffer + (bmp.Height - 1 - y) * PrinterNative.RowStride, row.Length);
            }
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            throw;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return buffer;
    }

    public static PrintConfig BuildConfig() => new()
    {
        StructVersion = 0x66,
        ModeFlags = 0xA1,
        Width = PrinterNative.PrintWidth,
        Height = PrinterNative.PrintHeight,
        CutLength = 1,
        Param14 = 1,
    };

    public static ImageDescriptor BuildDescriptor(IntPtr buffer) => new()
    {
        PixelBuffer = buffer,
        RowStride = PrinterNative.RowStride,
        Rect = new NativeRect { Right = PrinterNative.PrintWidth, Bottom = PrinterNative.PrintHeight },
    };

    /// <param name="loadLut">Load KM1_LUT.lut from DATA_FOLDER instead of the built-in table.</param>
    public static JobOptions BuildOptions(PrintJobSettings s, bool loadLut)
    {
        var o = new JobOptions
        {
            StructVersion = 0x68,
            Opt04 = -1,
            Opt06 = (short)(loadLut ? 0 : -1),
            Opt08 = -1,
            SharpnessMode = 4,
            Opt28 = -1,
            Opt2A = -1,
            Opt2C = -1,
            Opt2E = -1,
        };
        if (s.OvercoatIndex is >= 1 and <= 6)
        {
            // KMAT pattern: the density register selects KMAT_%02d.raw, so density is fixed.
            o.ColorConvMode = 0x0102;
            o.DensityCode = (short)(0x0100 + s.OvercoatIndex);
        }
        else
        {
            o.ColorConvMode = 0x0101;
            o.DensityCode = DensityCode(s.Density);
        }
        return o;
    }

    /// <summary>
    /// Glossy density code. soundvoltex.dll always sends 0x0101 for glossy cards, so density 0 does too.
    /// Other values use 0x0108 + density (as print_image.py does), clamped to the SDK's 0x0101..0x011E.
    /// </summary>
    public static short DensityCode(int density) =>
        density == 0 ? (short)0x0101 : (short)Math.Clamp(0x0108 + density, 0x0101, 0x011E);

    /// <summary>
    /// Portrait 714x1074 calibration card on black: colour bars, grey ramp, gradients, and rulers
    /// at each edge of the card face to measure how much the printer trims.
    /// </summary>
    public static Bitmap CreateTestCard()
    {
        const int W = CardCompositor.CardWidth, H = CardCompositor.CardHeight;
        var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Lay everything out on the card face (trim area), which is not centred in the 714x1074 art.
        var face = Rectangle.Round(CardCompositor.TrimRect);
        const int margin = 50;
        int x0 = face.Left + margin, w = face.Width - margin * 2;

        using var titleFont = new Font("Segoe UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var tinyFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        int y = face.Top + margin;
        g.DrawString("CP-T50D TEST CARD", titleFont, Brushes.White, x0, y);
        g.DrawString($"CP-T50D Tools  ·  {DateTime.Now:yyyy-MM-dd HH:mm}", smallFont, Brushes.Silver, x0, y + 40);

        // Primary / secondary colour bars
        Color[] bars = { Color.White, Color.Yellow, Color.Cyan, Color.Lime, Color.Magenta, Color.Red, Color.Blue, Color.Black };
        y += 90;
        int barH = 180, barW = w / bars.Length;
        for (int i = 0; i < bars.Length; i++)
        {
            using var b = new SolidBrush(bars[i]);
            g.FillRectangle(b, x0 + i * barW, y, i == bars.Length - 1 ? w - i * barW : barW, barH);
        }
        g.DrawRectangle(Pens.Gray, x0, y, w, barH);

        // 11-step grey ramp
        y += barH + 30;
        int steps = 11, stepW = w / steps;
        for (int i = 0; i < steps; i++)
        {
            int v = 255 - i * 255 / (steps - 1);
            using var b = new SolidBrush(Color.FromArgb(v, v, v));
            g.FillRectangle(b, x0 + i * stepW, y, i == steps - 1 ? w - i * stepW : stepW, 90);
        }
        g.DrawRectangle(Pens.Gray, x0, y, w, 90);

        // Smooth gradients for banding checks
        y += 120;
        void Gradient(Color c, int top)
        {
            using var b = new LinearGradientBrush(new Rectangle(x0, top, w, 60), Color.Black, c, LinearGradientMode.Horizontal);
            g.FillRectangle(b, x0, top, w, 60);
            g.DrawRectangle(Pens.DimGray, x0, top, w, 60);
        }
        Gradient(Color.Red, y);
        Gradient(Color.Lime, y + 70);
        Gradient(Color.Blue, y + 140);
        Gradient(Color.White, y + 210);

        // Fine line / resolution patch
        y += 300;
        for (int i = 0; i < 50; i++)
            g.FillRectangle(Brushes.White, x0 + i * 4, y, 1 + i % 2, 110);
        g.DrawString("Density 0 = SDVX default", smallFont, Brushes.White, x0 + 220, y + 5);
        g.DrawString("Edge rulers: count the lines left", smallFont, Brushes.White, x0 + 220, y + 35);
        g.DrawString("on each side (5 px apart) to see", smallFont, Brushes.White, x0 + 220, y + 60);
        g.DrawString("how much the printer trims.", smallFont, Brushes.White, x0 + 220, y + 85);

        // Trim-edge rulers: lines parallel to each face edge, every 5 px inward up to 40 px.
        using var rulerPen = new Pen(Color.White, 1f);
        using var majorPen = new Pen(Color.Yellow, 1f);
        const int rulerLen = 120, rulerMax = 40;
        int cx = face.Left + face.Width / 2, cy = face.Top + face.Height / 2;
        for (int d = 0; d <= rulerMax; d += 5)
        {
            var pen = d % 10 == 0 ? majorPen : rulerPen;
            g.DrawLine(pen, face.Left + d, cy - rulerLen / 2, face.Left + d, cy + rulerLen / 2);      // left
            g.DrawLine(pen, face.Right - 1 - d, cy - rulerLen / 2, face.Right - 1 - d, cy + rulerLen / 2); // right
            g.DrawLine(pen, cx - rulerLen / 2, face.Top + d, cx + rulerLen / 2, face.Top + d);        // top
            g.DrawLine(pen, cx - rulerLen / 2, face.Bottom - 1 - d, cx + rulerLen / 2, face.Bottom - 1 - d); // bottom
        }
        // Label every other yellow line; 10 px apart the labels would overlap.
        for (int d = 20; d <= rulerMax; d += 20)
        {
            string label = d.ToString();
            g.DrawString(label, tinyFont, Brushes.Yellow, face.Left + d - 6, cy + rulerLen / 2 + 2);
            g.DrawString(label, tinyFont, Brushes.Yellow, face.Right - 1 - d - 6, cy + rulerLen / 2 + 2);
            g.DrawString(label, tinyFont, Brushes.Yellow, cx + rulerLen / 2 + 4, face.Top + d - 7);
            g.DrawString(label, tinyFont, Brushes.Yellow, cx + rulerLen / 2 + 4, face.Bottom - 1 - d - 7);
        }

        // Corner marks at the trim line
        using var cornerPen = new Pen(Color.White, 2f);
        foreach (var (px, py, sx, sy) in new[] { (face.Left, face.Top, 1, 1), (face.Right - 1, face.Top, -1, 1),
                                                 (face.Left, face.Bottom - 1, 1, -1), (face.Right - 1, face.Bottom - 1, -1, -1) })
        {
            g.DrawLine(cornerPen, px, py, px + 30 * sx, py);
            g.DrawLine(cornerPen, px, py, px, py + 30 * sy);
        }
        return bmp;
    }
}
