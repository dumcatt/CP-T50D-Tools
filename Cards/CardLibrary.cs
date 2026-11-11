using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CPT50DTools.Ifs;

namespace CPT50DTools.Cards;

/// <summary>A single selectable card (an image from disk or from inside an IFS archive).</summary>
public sealed class CardItem
{
    public const int ThumbMaxWidth = 144;
    public const int ThumbMaxHeight = 217;

    public string Name { get; init; } = "";
    /// <summary>Human readable source description (file path / archive path).</summary>
    public string Source { get; init; } = "";
    public CardPack Pack { get; internal set; } = null!;

    /// <summary>Loads the full resolution image. Caller owns the returned bitmap.</summary>
    public Func<Bitmap> LoadFull { get; init; } = null!;
    /// <summary>Optional loader for a low-res version (e.g. from a *_s.ifs pack). Caller owns the bitmap.</summary>
    public Func<Bitmap>? LoadSmall { get; init; }

    /// <summary>Cached thumbnail (set by the thumbnail loader).</summary>
    public Bitmap? Thumbnail { get; set; }
    public bool ThumbnailFailed { get; set; }

    public Bitmap CreateThumbnail()
    {
        using var src = LoadSmall?.Invoke() ?? LoadFull();
        return Scale(src, ThumbMaxWidth, ThumbMaxHeight);
    }

    public static Bitmap Scale(Image src, int maxW, int maxH)
    {
        double s = Math.Min((double)maxW / src.Width, (double)maxH / src.Height);
        if (s > 1) s = 1;
        int w = Math.Max(1, (int)Math.Round(src.Width * s));
        int h = Math.Max(1, (int)Math.Round(src.Height * s));
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, w, h);
        return bmp;
    }

    private (GenesisCardCatalog catalog, GenesisCardInfo info)? _catalogInfo;

    /// <summary>This card's genesis_card.xml entry, cached until a different catalog is loaded.</summary>
    public GenesisCardInfo CatalogInfo()
    {
        var catalog = GenesisCardCatalog.Instance;
        var cached = _catalogInfo;
        if (cached is { } c && ReferenceEquals(c.catalog, catalog)) return c.info;
        var info = catalog.Lookup(Name);
        _catalogInfo = (catalog, info);
        return info;
    }

    public override string ToString() => Name;
}

/// <summary>A collapsible group of cards (an IFS pack or a set of loose images).</summary>
public sealed class CardPack
{
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";
    public List<CardItem> Cards { get; } = new();
    public bool Collapsed { get; set; }

    public void Add(CardItem c)
    {
        c.Pack = this;
        Cards.Add(c);
    }
}

/// <summary>Builds <see cref="CardPack"/>s from folders, IFS archives and image files.</summary>
public static class CardLibrary
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsIfsFile(string path) =>
        Path.GetExtension(path).Equals(".ifs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// If the chosen folder is a game root (contains contents\data\graphics\chara_card or
    /// data\graphics\chara_card) the card folder inside it is used instead.
    /// </summary>
    public static string ResolveCardFolder(string folder)
    {
        string? data = ResolveDataFolder(folder);
        if (data != null) return Path.Combine(data, "graphics", "chara_card");
        string nested = Path.Combine(folder, "chara_card");
        return Directory.Exists(nested) ? nested : folder;
    }

    private static bool IsDataFolder(string dir) =>
        Directory.Exists(Path.Combine(dir, "graphics", "chara_card"));

    /// <summary>
    /// Finds the game's "data" folder (the one holding graphics\chara_card and others\genesis_card.xml)
    /// from a game root, "contents", "data" folder, or any folder inside "data" (e.g. chara_card).
    /// Returns null if none is found.
    /// </summary>
    public static string? ResolveDataFolder(string path)
    {
        string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return null;
        dir = Path.GetFullPath(dir);

        // Look down from the chosen folder.
        foreach (var c in new[] { dir, Path.Combine(dir, "data"), Path.Combine(dir, "contents", "data") })
            if (IsDataFolder(c)) return c;

        // Look up from inside the data folder (e.g. data\graphics\chara_card).
        for (var d = Directory.GetParent(dir); d != null; d = d.Parent)
            if (IsDataFolder(d.FullName)) return d.FullName;
        return null;
    }

    /// <summary>Path of genesis_card.xml for a data folder, or null if it does not exist.</summary>
    public static string? CatalogPath(string dataFolder)
    {
        string p = Path.Combine(dataFolder, "others", "genesis_card.xml");
        return File.Exists(p) ? p : null;
    }

    /// <summary>Strips ".ifs" and a trailing "_b"/"_s" size suffix.</summary>
    public static string PackKey(string fileName, out char suffix)
    {
        string n = Path.GetFileNameWithoutExtension(fileName);
        suffix = '\0';
        if (n.Length > 2 && n[^2] == '_' && (n[^1] == 'b' || n[^1] == 's'))
        {
            suffix = n[^1];
            n = n[..^2];
        }
        return n;
    }

    private static string CardKey(string nestedName)
    {
        string n = Path.GetFileNameWithoutExtension(nestedName);
        return n.EndsWith("_s", StringComparison.OrdinalIgnoreCase) ? n[..^2] : n;
    }

    public static List<CardPack> LoadFolder(string folder, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var packs = new List<CardPack>();
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        // Pair *_b.ifs (big) with *_s.ifs (small) packs in the same directory.
        var ifsGroups = files.Where(IsIfsFile)
            .GroupBy(f => Path.Combine(Path.GetDirectoryName(f)!, PackKey(f, out _)), StringComparer.OrdinalIgnoreCase);

        foreach (var g in ifsGroups)
        {
            ct.ThrowIfCancellationRequested();
            string? big = null, small = null, plain = null;
            foreach (var f in g)
            {
                PackKey(f, out char sfx);
                if (sfx == 'b') big = f;
                else if (sfx == 's') small = f;
                else plain = f;
            }

            if (plain != null) TryAdd(packs, plain, null, progress);
            if (big != null || small != null) TryAdd(packs, big ?? small!, big != null ? small : null, progress);
        }

        // Loose images grouped by directory.
        foreach (var dirGroup in files.Where(IsImageFile).GroupBy(f => Path.GetDirectoryName(f)!))
        {
            ct.ThrowIfCancellationRequested();
            var pack = new CardPack { Name = Path.GetFileName(dirGroup.Key) + " (images)", Source = dirGroup.Key };
            foreach (var f in dirGroup) pack.Add(FromImageFile(f));
            packs.Add(pack);
        }
        return packs;
    }

    private static void TryAdd(List<CardPack> packs, string primary, string? small, IProgress<string>? progress)
    {
        progress?.Report($"Reading {Path.GetFileName(primary)}...");
        try
        {
            var pack = LoadIfs(primary, small);
            if (pack.Cards.Count > 0) packs.Add(pack);
        }
        catch (Exception ex)
        {
            progress?.Report($"Failed to read {Path.GetFileName(primary)}: {ex.Message}");
        }
    }

    /// <summary>Loads a single file (IFS or image). For *_b.ifs the matching *_s.ifs is used for thumbnails.</summary>
    public static CardPack LoadFile(string path)
    {
        if (IsImageFile(path))
        {
            var pack = new CardPack { Name = Path.GetFileName(path), Source = path };
            pack.Add(FromImageFile(path));
            return pack;
        }

        string? small = null;
        string key = PackKey(path, out char sfx);
        if (sfx == 'b')
        {
            string candidate = Path.Combine(Path.GetDirectoryName(path)!, key + "_s.ifs");
            if (File.Exists(candidate)) small = candidate;
        }
        return LoadIfs(path, small);
    }

    public static CardItem FromImageFile(string path) => new()
    {
        Name = Path.GetFileName(path),
        Source = path,
        LoadFull = () =>
        {
            using var raw = LoadImageFile(path);
            if (raw.Width == 714 && raw.Height == 1074)
            {
                return CardCompositor.Compose(raw, Path.GetFileName(path));
            }
            return (Bitmap)raw.Clone();
        },
    };

    private static Bitmap LoadImageFile(string path)
    {
        // Load via a memory copy so the file is not locked.
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        using var img = Image.FromStream(ms);
        return new Bitmap(img);
    }

    public static CardPack LoadIfs(string path, string? smallPath = null)
    {
        var archive = IfsArchive.Open(path);
        IfsArchive? smallArchive = null;
        if (smallPath != null)
        {
            try { smallArchive = IfsArchive.Open(smallPath); } catch { /* thumbnails fall back to full */ }
        }

        var pack = new CardPack { Name = Path.GetFileName(path), Source = path };

        // Images stored directly in this archive (a single-card IFS).
        foreach (var img in archive.Images)
        {
            var captured = img;
            var smallImg = smallArchive?.Images.FirstOrDefault(i => CardKey(i.Name) == CardKey(img.Name));
            pack.Add(new CardItem
            {
                Name = archive.Images.Count == 1 ? Path.GetFileName(path) : img.Name,
                Source = $"{path}::{img.File.Path}",
                LoadFull = () => LoadCompositeOrSingle(archive, captured, smallArchive, smallImg),
                LoadSmall = smallImg != null ? () => smallArchive!.DecodeImage(smallImg) : null,
            });
        }

        // Nested card archives (card packs contain one .ifs per card).
        var smallMap = smallArchive?.NestedArchives
            .GroupBy(e => CardKey(e.Name))
            .ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<string, IfsEntry>();

        foreach (var nested in archive.NestedArchives.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entry = nested;
            smallMap.TryGetValue(CardKey(entry.Name), out var smallEntry);
            pack.Add(new CardItem
            {
                Name = entry.Name,
                Source = $"{path}::{entry.Path}",
                LoadFull = () => LoadCompositeOrNested(archive, entry, smallArchive, smallEntry),
                LoadSmall = smallEntry != null ? () => DecodeBest(smallArchive!.OpenNested(smallEntry)) : null,
            });
        }

        return pack;
    }

    private static Bitmap LoadCompositeOrNested(IfsArchive archive, IfsEntry entry, IfsArchive? smallArchive, IfsEntry? smallEntry)
    {
        using var big = DecodeBest(archive.OpenNested(entry));
        Bitmap? small = null;
        if (smallArchive != null && smallEntry != null)
        {
            try { small = DecodeBest(smallArchive.OpenNested(smallEntry)); } catch { }
        }

        try
        {
            return CardCompositor.Compose(big, entry.Name, small);
        }
        finally
        {
            small?.Dispose();
        }
    }

    private static Bitmap LoadCompositeOrSingle(IfsArchive archive, IfsImage img, IfsArchive? smallArchive, IfsImage? smallImg)
    {
        using var big = archive.DecodeImage(img);
        Bitmap? small = null;
        if (smallArchive != null && smallImg != null)
        {
            try { small = smallArchive.DecodeImage(smallImg); } catch { }
        }

        try
        {
            return CardCompositor.Compose(big, img.Name, small);
        }
        finally
        {
            small?.Dispose();
        }
    }

    /// <summary>
    /// Composites high-resolution character artwork from the *_b.ifs archive with authentic
    /// native vector-grade card frames, title/artist plaques, and crest logos.
    /// </summary>
    public static Bitmap CompositeCard(Bitmap big, Bitmap? small, string cardName = "")
    {
        return CardCompositor.Compose(big, cardName, small);
    }

    /// <summary>Decodes the largest image of an archive, searching nested archives if needed.</summary>
    private static Bitmap DecodeBest(IfsArchive a)
    {
        var best = a.Images.OrderByDescending(i => i.Width * i.Height).FirstOrDefault();
        if (best != null) return a.DecodeImage(best);
        foreach (var n in a.NestedArchives)
        {
            try { return DecodeBest(a.OpenNested(n)); } catch { }
        }
        throw new InvalidDataException("No images found in archive");
    }
}
