using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CPT50DTools.Ifs;

/// <summary>A file or folder inside an IFS archive.</summary>
public sealed class IfsEntry
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsFolder { get; init; }
    /// <summary>Absolute offset of the file data within the .ifs file.</summary>
    public long Offset { get; init; }
    public int Size { get; init; }
    public List<IfsEntry> Children { get; } = new();

    public IEnumerable<IfsEntry> AllFiles()
    {
        foreach (var c in Children)
        {
            if (c.IsFolder) foreach (var f in c.AllFiles()) yield return f;
            else yield return c;
        }
    }

    public override string ToString() => Path;
}

/// <summary>A texture image described by a tex/texturelist.xml inside an IFS.</summary>
public sealed class IfsImage
{
    public string Name { get; init; } = "";
    public string TextureName { get; init; } = "";
    public string Format { get; init; } = "";
    public string? Compression { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public IfsEntry File { get; init; } = null!;
    /// <summary>True when the entry is a plain encoded image (png/jpg/...) stored inside the IFS.</summary>
    public bool IsEncodedImage { get; init; }

    public override string ToString() => $"{Name} ({Width}x{Height}, {Format})";
}

/// <summary>
/// Read-only IFS archive reader. Ported from mon/ifstools (https://github.com/mon/ifstools).
/// Only the header/manifest is read on open; file data is read on demand.
/// </summary>
public sealed class IfsArchive
{
    private const uint IfsSignature = 0x6CAD8F89;
    private static readonly string[] EncodedImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    public string FilePath { get; }
    /// <summary>Offset of this archive within <see cref="FilePath"/> (non-zero for nested IFS files).</summary>
    public long BaseOffset { get; }
    public IfsEntry Root { get; private set; } = null!;
    public List<IfsImage> Images { get; } = new();
    public ushort Version { get; private set; }

    /// <summary>Nested .ifs files contained in this archive.</summary>
    public IEnumerable<IfsEntry> NestedArchives =>
        Root.AllFiles().Where(f => f.Name.EndsWith(".ifs", StringComparison.OrdinalIgnoreCase));

    private IfsArchive(string path, long baseOffset)
    {
        FilePath = path;
        BaseOffset = baseOffset;
    }

    public static bool IsIfs(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> sig = stackalloc byte[4];
            return fs.Read(sig) == 4 && BinaryPrimitives.ReadUInt32BigEndian(sig) == IfsSignature;
        }
        catch { return false; }
    }

    public static IfsArchive Open(string path)
    {
        var ifs = new IfsArchive(path, 0);
        ifs.Load();
        return ifs;
    }

    /// <summary>Opens an .ifs file stored inside this archive without extracting it.</summary>
    public IfsArchive OpenNested(IfsEntry entry)
    {
        var ifs = new IfsArchive(FilePath, entry.Offset);
        ifs.Load();
        return ifs;
    }

    private void Load()
    {
        using var fs = File.OpenRead(FilePath);
        fs.Seek(BaseOffset, SeekOrigin.Begin);
        var header = new byte[36];
        fs.ReadExactly(header, 0, 20);

        uint sig = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (sig != IfsSignature) throw new InvalidDataException("Not an IFS file (bad signature)");
        Version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        ushort versionCheck = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6));
        if ((Version ^ 0xFFFF) != versionCheck) throw new InvalidDataException("IFS version check failed");
        // uint time = header[8..12]; uint treeSize = header[12..16];
        uint manifestEnd = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16));

        int headerEnd = 20;
        if (Version > 1) headerEnd += 16; // manifest md5

        var manifest = new byte[manifestEnd - headerEnd];
        fs.Seek(BaseOffset + headerEnd, SeekOrigin.Begin);
        fs.ReadExactly(manifest);

        var tree = KBinXml.Decode(manifest);
        Root = new IfsEntry { Name = "", Path = "", IsFolder = true };
        BuildTree(tree, Root, BaseOffset + manifestEnd);

        LoadImages(Root);
    }

    private static string FixName(string n)
    {
        n = n.Replace("_E", ".").Replace("__", "_");
        if (n.Length > 1 && n[0] == '_' && char.IsDigit(n[1])) n = n[1..];
        return n;
    }

    private static void BuildTree(KNode node, IfsEntry folder, long dataStart)
    {
        foreach (var child in node.Children)
        {
            string name = FixName(child.Name);
            string path = string.IsNullOrEmpty(folder.Path) ? name : folder.Path + "/" + name;

            bool isFile = child.Children.Count == 0 && child.Values.Length >= 2 && child.Type != "void";
            if (isFile)
            {
                var v = child.Values;
                folder.Children.Add(new IfsEntry
                {
                    Name = name,
                    Path = path,
                    Offset = dataStart + Convert.ToInt64(v[0]),
                    Size = Convert.ToInt32(v[1]),
                });
            }
            else if (child.Children.Count > 0 || child.Type == "void")
            {
                var sub = new IfsEntry { Name = name, Path = path, IsFolder = true };
                folder.Children.Add(sub);
                BuildTree(child, sub, dataStart);
            }
        }
    }

    public byte[] ReadEntry(IfsEntry entry)
    {
        using var fs = File.OpenRead(FilePath);
        fs.Seek(entry.Offset, SeekOrigin.Begin);
        var data = new byte[entry.Size];
        fs.ReadExactly(data);
        return data;
    }

    private static KNode ParseXmlDocument(byte[] data)
    {
        if (KBinXml.IsKBin(data)) return KBinXml.Decode(data);
        var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
        return FromXElement(doc.Root!, null);
    }

    private static KNode FromXElement(XElement e, KNode? parent)
    {
        var n = new KNode { Name = e.Name.LocalName, Parent = parent };
        foreach (var a in e.Attributes()) n.Attributes[a.Name.LocalName] = a.Value;
        if (!e.HasElements && !string.IsNullOrWhiteSpace(e.Value))
        {
            n.Type = e.Attribute("__type")?.Value ?? "str";
            n.Text = e.Value;
            if (n.Type != "str")
                n.Values = e.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => (object)(long.TryParse(s, out var l) ? l : 0L)).ToArray();
        }
        foreach (var c in e.Elements()) n.Children.Add(FromXElement(c, n));
        return n;
    }

    private static string Md5Hex(string s)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void LoadImages(IfsEntry folder)
    {
        foreach (var child in folder.Children)
        {
            if (!child.IsFolder)
            {
                string ext = System.IO.Path.GetExtension(child.Name).ToLowerInvariant();
                if (EncodedImageExts.Contains(ext))
                {
                    Images.Add(new IfsImage
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(child.Name),
                        Format = ext.TrimStart('.'),
                        File = child,
                        IsEncodedImage = true,
                    });
                }
                continue;
            }

            if (child.Name == "tex") LoadTexFolder(child);
            else LoadImages(child);
        }
    }

    private void LoadTexFolder(IfsEntry tex)
    {
        var xmlFile = tex.Children.FirstOrDefault(c => !c.IsFolder && c.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (xmlFile == null) return;

        KNode texList;
        try { texList = ParseXmlDocument(ReadEntry(xmlFile)); }
        catch { return; }

        var files = tex.Children.Where(c => !c.IsFolder).ToDictionary(c => c.Name, c => c);
        string? listCompress = texList.Attr("compress");

        foreach (var texture in texList.Descendants("texture"))
        {
            string format = texture.Attr("format") ?? "argb8888rev";
            string? compress = texture.Attr("compress") ?? listCompress;
            foreach (var image in texture.Children.Where(c => c.Name == "image"))
            {
                string? name = image.Attr("name");
                if (name == null) continue;

                if (!files.TryGetValue(name, out var file) && !files.TryGetValue(Md5Hex(name), out file))
                    continue;

                var rect = image.Child("imgrect")?.IntValues();
                int w = 0, h = 0;
                if (rect is { Length: 4 })
                {
                    w = (rect[1] - rect[0]) / 2;
                    h = (rect[3] - rect[2]) / 2;
                }

                Images.Add(new IfsImage
                {
                    Name = name,
                    TextureName = texture.Attr("name") ?? "",
                    Format = format,
                    Compression = compress,
                    Width = w,
                    Height = h,
                    File = file,
                });
            }
        }
    }

    /// <summary>Decodes an image into a 32bpp ARGB bitmap.</summary>
    public Bitmap DecodeImage(IfsImage img)
    {
        var data = ReadEntry(img.File);

        if (img.IsEncodedImage)
        {
            using var ms = new MemoryStream(data);
            using var tmp = Image.FromStream(ms);
            return new Bitmap(tmp);
        }

        if (img.Compression == "avslz" && data.Length >= 8)
        {
            int uncompressedSize = BinaryPrimitives.ReadInt32BigEndian(data);
            int compressedSize = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
            if (data.Length == compressedSize + 8)
            {
                data = Lz77.Decompress(data.AsSpan(8), uncompressedSize);
            }
            else
            {
                // "Compressed" header present but data stored raw: the 8 bytes are moved to the end.
                var fixedData = new byte[data.Length];
                Buffer.BlockCopy(data, 8, fixedData, 0, data.Length - 8);
                Buffer.BlockCopy(data, 0, fixedData, data.Length - 8, 8);
                data = fixedData;
            }
        }

        return PixelDecoder.Decode(data, img.Width, img.Height, img.Format);
    }
}

internal static class PixelDecoder
{
    public static Bitmap Decode(byte[] data, int w, int h, string format)
    {
        if (w <= 0 || h <= 0) throw new InvalidDataException("Image has no size information");
        var bgra = new byte[w * h * 4];
        int px = w * h;

        switch (format.ToLowerInvariant())
        {
            case "argb8888rev": // stored B,G,R,A - same as GDI+ memory layout
                Buffer.BlockCopy(data, 0, bgra, 0, Math.Min(data.Length, bgra.Length));
                break;
            case "argb8888": // stored A,R,G,B
                for (int i = 0; i < px && i * 4 + 3 < data.Length; i++)
                {
                    bgra[i * 4 + 0] = data[i * 4 + 3];
                    bgra[i * 4 + 1] = data[i * 4 + 2];
                    bgra[i * 4 + 2] = data[i * 4 + 1];
                    bgra[i * 4 + 3] = data[i * 4 + 0];
                }
                break;
            case "argb4444":
                for (int i = 0; i < px && i * 2 + 1 < data.Length; i++)
                {
                    int v = data[i * 2] | (data[i * 2 + 1] << 8);
                    bgra[i * 4 + 0] = (byte)((v & 0xF) * 17);
                    bgra[i * 4 + 1] = (byte)(((v >> 4) & 0xF) * 17);
                    bgra[i * 4 + 2] = (byte)(((v >> 8) & 0xF) * 17);
                    bgra[i * 4 + 3] = (byte)(((v >> 12) & 0xF) * 17);
                }
                break;
            case "dxt1":
            case "dxt5":
                DxtDecoder.Decode(data, w, h, format == "dxt5", bgra);
                break;
            default:
                throw new NotSupportedException($"Unsupported texture format '{format}'");
        }

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
            Marshal.Copy(bgra, y * w * 4, bd.Scan0 + y * bd.Stride, w * 4);
        bmp.UnlockBits(bd);
        return bmp;
    }
}

internal static class DxtDecoder
{
    public static void Decode(byte[] data, int w, int h, bool dxt5, byte[] bgra)
    {
        int blockSize = dxt5 ? 16 : 8;
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        var colors = new byte[4 * 4];
        var alphas = new byte[8];
        int pos = 0;
        for (int by = 0; by < bh; by++)
        for (int bx = 0; bx < bw; bx++)
        {
            if (pos + blockSize > data.Length) return;
            ulong alphaBits = 0;
            if (dxt5)
            {
                alphas[0] = data[pos]; alphas[1] = data[pos + 1];
                for (int i = 0; i < 6; i++) alphaBits |= (ulong)data[pos + 2 + i] << (8 * i);
                if (alphas[0] > alphas[1])
                    for (int i = 1; i < 7; i++) alphas[i + 1] = (byte)(((7 - i) * alphas[0] + i * alphas[1]) / 7);
                else
                {
                    for (int i = 1; i < 5; i++) alphas[i + 1] = (byte)(((5 - i) * alphas[0] + i * alphas[1]) / 5);
                    alphas[6] = 0; alphas[7] = 255;
                }
                pos += 8;
            }
            int c0 = data[pos] | (data[pos + 1] << 8);
            int c1 = data[pos + 2] | (data[pos + 3] << 8);
            uint idx = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            pos += 8;
            Rgb565(c0, colors, 0); Rgb565(c1, colors, 4);
            colors[3] = colors[7] = 255;
            bool fourColor = c0 > c1 || dxt5;
            for (int k = 0; k < 3; k++)
            {
                if (fourColor)
                {
                    colors[8 + k] = (byte)((2 * colors[k] + colors[4 + k]) / 3);
                    colors[12 + k] = (byte)((colors[k] + 2 * colors[4 + k]) / 3);
                }
                else
                {
                    colors[8 + k] = (byte)((colors[k] + colors[4 + k]) / 2);
                    colors[12 + k] = 0;
                }
            }
            colors[11] = 255;
            colors[15] = (byte)(fourColor ? 255 : 0);

            for (int py = 0; py < 4; py++)
            for (int pxl = 0; pxl < 4; pxl++)
            {
                int x = bx * 4 + pxl, y = by * 4 + py;
                if (x >= w || y >= h) continue;
                int i = py * 4 + pxl;
                int ci = (int)((idx >> (2 * i)) & 3);
                int o = (y * w + x) * 4;
                bgra[o] = colors[ci * 4]; bgra[o + 1] = colors[ci * 4 + 1]; bgra[o + 2] = colors[ci * 4 + 2];
                bgra[o + 3] = dxt5 ? alphas[(int)((alphaBits >> (3 * i)) & 7)] : colors[ci * 4 + 3];
            }
        }
    }

    private static void Rgb565(int c, byte[] o, int off)
    {
        o[off + 2] = (byte)(((c >> 11) & 0x1F) * 255 / 31);
        o[off + 1] = (byte)(((c >> 5) & 0x3F) * 255 / 63);
        o[off + 0] = (byte)((c & 0x1F) * 255 / 31);
    }
}
