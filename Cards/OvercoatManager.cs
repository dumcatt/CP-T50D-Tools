using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CPT50DTools.Cards;

/// <summary>
/// Loads and renders KMAT overcoat energy map patterns (kmat_01.raw .. kmat_06.raw).
/// Each raw file is a 1072x674 16-bit little-endian array of thermal energy values.
/// Base glossy value is ~16540; values > 16540 produce matte / holographic watermarks.
/// </summary>
public sealed class OvercoatManager
{
    private static OvercoatManager? _instance;
    public static OvercoatManager Instance => _instance ??= new OvercoatManager();

    private readonly Dictionary<int, Bitmap> _portraitCache = new();
    private readonly Dictionary<int, Bitmap> _landscapeCache = new();
    private string? _currentDataDir;

    private const int RawWidth = 1072;
    private const int RawHeight = 674;
    private const ushort BaseGlossy = 16540;

    public void SetDataDirectory(string? dir)
    {
        if (string.Equals(_currentDataDir, dir, StringComparison.OrdinalIgnoreCase)) return;
        ClearCache();
        _currentDataDir = dir;
    }

    private void ClearCache()
    {
        foreach (var bmp in _portraitCache.Values) bmp.Dispose();
        foreach (var bmp in _landscapeCache.Values) bmp.Dispose();
        _portraitCache.Clear();
        _landscapeCache.Clear();
    }

    /// <summary>
    /// Gets a 32bpp ARGB watermark overlay for the specified pattern (1..6).
    /// </summary>
    public Bitmap? GetOvercoatMask(int patternIndex, bool portrait)
    {
        if (patternIndex < 1 || patternIndex > 6) return null;
        var cache = portrait ? _portraitCache : _landscapeCache;
        if (cache.TryGetValue(patternIndex, out var cached)) return cached;

        var rawBmp = LoadRawPattern(patternIndex);
        if (rawBmp == null) return null;

        Bitmap finalBmp;
        if (portrait)
        {
            // Landscape raw (1072x674) rotated 270 deg (90 deg counter-clockwise) -> 674x1072
            finalBmp = (Bitmap)rawBmp.Clone();
            finalBmp.RotateFlip(RotateFlipType.Rotate270FlipNone);
            rawBmp.Dispose();
        }
        else
        {
            finalBmp = rawBmp;
        }

        cache[patternIndex] = finalBmp;
        return finalBmp;
    }

    private Bitmap? LoadRawPattern(int patternIndex)
    {
        string[] candidates =
        {
            _currentDataDir != null ? Path.Combine(_currentDataDir, $"kmat_{patternIndex:D2}.raw") : "",
            _currentDataDir != null ? Path.Combine(_currentDataDir, $"KMAT_{patternIndex:D2}.raw") : "",
            Path.Combine(AppContext.BaseDirectory, "KFC", "ext", "cpusbxpkm_data", $"kmat_{patternIndex:D2}.raw"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "KFC", "ext", "cpusbxpkm_data", $"kmat_{patternIndex:D2}.raw"),
            Path.Combine(Environment.CurrentDirectory, "KFC", "ext", "cpusbxpkm_data", $"kmat_{patternIndex:D2}.raw"),
        };

        string? filePath = candidates.FirstOrDefault(f => !string.IsNullOrEmpty(f) && File.Exists(f));
        if (filePath == null) return null;

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length < RawWidth * RawHeight * 2) return null;

            var span = MemoryMarshal.Cast<byte, ushort>(bytes.AsSpan(0, RawWidth * RawHeight * 2));
            ushort maxVal = BaseGlossy;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] > maxVal) maxVal = span[i];
            }

            int diff = Math.Max(1, maxVal - BaseGlossy);
            var bgra = new byte[RawWidth * RawHeight * 4];

            for (int i = 0; i < span.Length; i++)
            {
                ushort val = span[i];
                if (val > BaseGlossy)
                {
                    double ratio = Math.Min(1.0, (double)(val - BaseGlossy) / diff);
                    // Silvery / holographic pearlescent highlight
                    byte alpha = (byte)Math.Clamp((int)(ratio * 140), 20, 180);
                    int o = i * 4;
                    bgra[o + 0] = 255; // B
                    bgra[o + 1] = 250; // G
                    bgra[o + 2] = 245; // R
                    bgra[o + 3] = alpha;
                }
            }

            var bmp = new Bitmap(RawWidth, RawHeight, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, RawWidth, RawHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bgra, 0, bd.Scan0, bgra.Length);
            bmp.UnlockBits(bd);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Overlays the active overcoat pattern onto the specified target graphics within targetRect.
    /// </summary>
    public void RenderOvercoat(Graphics g, Rectangle targetRect, int patternIndex, bool portrait)
    {
        if (patternIndex < 1 || patternIndex > 6) return;
        var mask = GetOvercoatMask(patternIndex, portrait);
        if (mask == null) return;

        // Mask is 674x1072 for portrait, or 1072x674 for landscape.
        // It centers in the 712x1072 (or 1072x712) card area.
        Rectangle dst;
        if (portrait)
        {
            double scale = (double)targetRect.Height / 1072.0;
            int mw = (int)Math.Round(674 * scale);
            int mh = targetRect.Height;
            int mx = targetRect.X + (targetRect.Width - mw) / 2;
            dst = new Rectangle(mx, targetRect.Y, mw, mh);
        }
        else
        {
            double scale = (double)targetRect.Width / 1072.0;
            int mw = targetRect.Width;
            int mh = (int)Math.Round(674 * scale);
            int my = targetRect.Y + (targetRect.Height - mh) / 2;
            dst = new Rectangle(targetRect.X, my, mw, mh);
        }

        var prevMode = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(mask, dst);
        g.InterpolationMode = prevMode;
    }
}
