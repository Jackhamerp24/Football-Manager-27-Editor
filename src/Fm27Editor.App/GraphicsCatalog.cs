using System.Text.Json;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Fm27Editor.App;

/// <summary>FM graphics config.xml mappings; indexing never reads or changes the game process.</summary>
sealed class GraphicsCatalog
{
    /// <summary>Largest side a picture is decoded to. Pictures are shown at 72 px (144 px on HiDPI); the source files are 200 px.</summary>
    const int MaxDecodeWidth = 144;
    /// <summary>Decoded pictures kept at once. Least recently used ones are dropped; a picture on screen keeps its own reference.</summary>
    const int MaxCachedBitmaps = 384;

    readonly LruCache<string, Bitmap> bitmaps = new(MaxCachedBitmaps);
    readonly Dictionary<int, Bitmap> flags = new();
    readonly List<(WeakReference<Image> Image, string Kind, int Id)> views = [];
    int viewsPruneAt = 256;
    GraphicsIndex index = GraphicsIndex.Empty;
    int generation;
    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "graphics.json");
    public string? Folder { get; private set; }

    public GraphicsCatalog()
    {
        try { if (File.Exists(SettingsPath)) Folder = JsonSerializer.Deserialize<string>(File.ReadAllText(SettingsPath)); } catch (Exception) { }
        if (string.IsNullOrWhiteSpace(Folder))
        {
            var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 27", "graphics");
            try
            {
                if (Directory.Exists(defaultFolder) && Directory.EnumerateFileSystemEntries(defaultFolder).Any())
                    Folder = defaultFolder;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task<bool> ChooseAsync(Window owner)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Graphics folder", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return false;
        Folder = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Folder));
        await LoadAsync();
        return true;
    }

    public async Task LoadAsync()
    {
        var folder = Folder;
        int version = ++generation;
        var result = await Task.Run(() => GraphicsIndex.Scan(folder));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version != generation) return;
            index = result;
            bitmaps.Clear();
            flags.Clear();
            for (int i = views.Count - 1; i >= 0; i--)
            {
                if (!views[i].Image.TryGetTarget(out var image)) { views.RemoveAt(i); continue; }
                if (views[i].Kind == "nationflag")
                {
                    image.Source = ResolveFlag(views[i].Id);
                }
                else
                {
                    string key = ResolveKey(views[i].Kind, views[i].Id);
                    image.Source = Resolve(key);
                }
                image.IsVisible = image.Source is not null;
            }
        });
    }

    public Image NationFlag(int? id, double height = 14)
    {
        double width = Math.Round(height * (25.0 / 18.0));
        var image = new Image
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false
        };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        if (id is not { } value || value <= 0) return image;
        image.Source = ResolveFlag(value);
        image.IsVisible = image.Source is not null;
        Track(image, "nationflag", value);
        return image;
    }

    internal Bitmap? ResolveFlag(int id)
    {
        if (flags.TryGetValue(id, out var cached)) return cached;

        // 1. If custom graphics pack explicitly has a flag for this nation (not logo!)
        string flagKey = $"graphics/pictures/nation/{id}/flag";
        if (index.Contains(flagKey))
        {
            foreach (var candidate in index.Paths(flagKey))
            foreach (var path in Path.HasExtension(candidate) ? new[] { candidate } : new[] { candidate + ".png", candidate + ".jpg", candidate + ".jpeg" })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using var stream = File.OpenRead(path);
                    return flags[id] = new Bitmap(stream);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }

        // 2. Load from embedded authentic national flags
        try
        {
            var uri = new Uri($"avares://Fm27Editor/Assets/Flags/{id}.png");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                return flags[id] = new Bitmap(stream);
            }
        }
        catch (Exception) { }

        return null;
    }

    public Image Image(string kind, int? id, double size)
    {
        if (kind == "nationflag")
            return NationFlag(id, size);

        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, IsVisible = false };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        if (id is not { } value || value <= 0) return image;
        string key = ResolveKey(kind, value);
        image.Source = Resolve(key);
        image.IsVisible = image.Source is not null;
        Track(image, kind, value);
        return image;
    }

    /// <summary>Remembers an image so a folder change can refresh it. Dead entries are dropped whenever the list doubles.</summary>
    void Track(Image image, string kind, int value)
    {
        views.Add((new WeakReference<Image>(image), kind, value));
        if (views.Count < viewsPruneAt) return;
        views.RemoveAll(v => !v.Image.TryGetTarget(out _));
        viewsPruneAt = Math.Max(256, views.Count * 2);
    }

    string ResolveKey(string kind, int value)
    {
        if (kind == "nationflag")
        {
            string flagKey = $"graphics/pictures/nation/{value}/flag";
            if (index.Contains(flagKey)) return flagKey;
            string logoKey = $"graphics/pictures/nation/{value}/logo";
            if (index.Contains(logoKey)) return logoKey;
            string iconKey = $"graphics/pictures/nation/{value}/icon";
            if (index.Contains(iconKey)) return iconKey;
            return flagKey;
        }
        if (kind == "nation")
        {
            string logoKey = $"graphics/pictures/nation/{value}/logo";
            if (index.Contains(logoKey)) return logoKey;
            string flagKey = $"graphics/pictures/nation/{value}/flag";
            if (index.Contains(flagKey)) return flagKey;
            return logoKey;
        }
        if (kind == "person")
        {
            return $"graphics/pictures/person/{value}/portrait";
        }
        if (kind == "comp")
        {
            string compKey = $"graphics/pictures/comp/{value}/logo";
            if (index.Contains(compKey)) return compKey;
            string competitionKey = $"graphics/pictures/competition/{value}/logo";
            if (index.Contains(competitionKey)) return competitionKey;
            return compKey;
        }
        if (kind == "award")
        {
            string awardKey = $"graphics/pictures/award/{value}/logo";
            if (index.Contains(awardKey)) return awardKey;
            string trophyKey = $"graphics/pictures/trophy/{value}/logo";
            if (index.Contains(trophyKey)) return trophyKey;
            return awardKey;
        }
        if (kind == "stadium")
        {
            string stadiumKey = $"graphics/pictures/stadium/{value}/logo";
            if (index.Contains(stadiumKey)) return stadiumKey;
            string pitchKey = $"graphics/pictures/stadium/{value}/pitch";
            if (index.Contains(pitchKey)) return pitchKey;
            return stadiumKey;
        }
        return $"graphics/pictures/{kind}/{value}/logo";
    }

    internal static unsafe Bitmap AutoCrop(WriteableBitmap wb)
    {
        int width = wb.PixelSize.Width;
        int height = wb.PixelSize.Height;
        if (width <= 2 || height <= 2) return wb;

        int minX = width, maxX = -1, minY = height, maxY = -1;
        using (var fb = wb.Lock())
        {
            byte* pBase = (byte*)fb.Address;
            int stride = fb.RowBytes;

            for (int y = 0; y < height; y++)
            {
                byte* row = pBase + y * stride;
                for (int x = 0; x < width; x++)
                {
                    if (row[x * 4 + 3] > 15)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }

        if (maxX < minX || maxY < minY) return wb;

        minX = Math.Max(0, minX - 2);
        minY = Math.Max(0, minY - 2);
        maxX = Math.Min(width - 1, maxX + 2);
        maxY = Math.Min(height - 1, maxY + 2);

        int cropWidth = maxX - minX + 1;
        int cropHeight = maxY - minY + 1;

        if (cropWidth == width && cropHeight == height) return wb;

        var cropped = new WriteableBitmap(
            new PixelSize(cropWidth, cropHeight),
            wb.Dpi,
            wb.Format ?? PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var srcFb = wb.Lock())
        using (var dstFb = cropped.Lock())
        {
            byte* srcBase = (byte*)srcFb.Address;
            byte* dstBase = (byte*)dstFb.Address;
            int srcStride = srcFb.RowBytes;
            int dstStride = dstFb.RowBytes;
            int bytesToCopy = cropWidth * 4;

            for (int y = 0; y < cropHeight; y++)
            {
                byte* pSrc = srcBase + (minY + y) * srcStride + minX * 4;
                byte* pDst = dstBase + y * dstStride;
                Buffer.MemoryCopy(pSrc, pDst, bytesToCopy, bytesToCopy);
            }
        }

        return cropped;
    }

    Bitmap? Resolve(string key)
    {
        if (bitmaps.TryGetValue(key, out var cached)) return cached;
        if (!index.Contains(key)) return null;
        foreach (var candidate in index.Paths(key))
        foreach (var path in Path.HasExtension(candidate) ? new[] { candidate } : new[] { candidate + ".png", candidate + ".jpg", candidate + ".jpeg" })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var stream = File.OpenRead(path);
                try
                {
                    var wb = WriteableBitmap.DecodeToWidth(stream, MaxDecodeWidth, BitmapInterpolationMode.HighQuality);
                    var result = AutoCrop(wb);
                    bitmaps.Add(key, result);
                    return result;
                }
                catch
                {
                    stream.Position = 0;
                    var result = Bitmap.DecodeToWidth(stream, MaxDecodeWidth, BitmapInterpolationMode.HighQuality);
                    bitmaps.Add(key, result);
                    return result;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }
}
