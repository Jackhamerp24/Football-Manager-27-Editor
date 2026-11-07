using System.Text.Json;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Fm27Editor.App;

/// <summary>FM graphics config.xml mappings; indexing never reads or changes the game process.</summary>
sealed class GraphicsCatalog
{
    readonly Dictionary<string, Bitmap> bitmaps = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(WeakReference<Image> Image, string Kind, int Id)> views = [];
    Dictionary<string, List<string>> index = new(StringComparer.OrdinalIgnoreCase);
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
        var result = await Task.Run(() => Scan(folder));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version != generation) return;
            index = result;
            bitmaps.Clear();
            for (int i = views.Count - 1; i >= 0; i--)
            {
                if (!views[i].Image.TryGetTarget(out var image)) { views.RemoveAt(i); continue; }
                string key = ResolveKey(views[i].Kind, views[i].Id);
                image.Source = Resolve(key);
                image.IsVisible = image.Source is not null;
            }
        });
    }

    internal static Dictionary<string, List<string>> Scan(string? folder)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var config in Directory.EnumerateFiles(folder, "config.xml", options).OrderBy(p => p.Contains("small", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var directory = Path.GetDirectoryName(config)!;
                    using var reader = XmlReader.Create(config, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true });
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Name != "record") continue;
                        var from = reader.GetAttribute("from"); var to = reader.GetAttribute("to");
                        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || Path.IsPathRooted(from)) continue;
                        var key = to.Replace('\\', '/').Trim('/');
                        if (!key.StartsWith("graphics/pictures/", StringComparison.OrdinalIgnoreCase)) continue;
                        if (key.StartsWith("graphics/pictures/person/r-", StringComparison.OrdinalIgnoreCase))
                            key = "graphics/pictures/person/" + key["graphics/pictures/person/r-".Length..];
                        var path = Path.GetFullPath(Path.Combine(directory, from));
                        if (!path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!result.TryGetValue(key, out var candidates)) result[key] = candidates = [];
                        candidates.Add(path);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException or NotSupportedException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result;
    }

    public Image Image(string kind, int? id, double size)
    {
        double width = kind == "nationflag" ? size * 1.4 : size;
        var image = new Image { Width = width, Height = size, Stretch = Stretch.Uniform, IsVisible = false };
        if (id is not { } value || value <= 0) return image;
        string key = ResolveKey(kind, value);
        image.Source = Resolve(key);
        image.IsVisible = image.Source is not null;
        views.Add((new WeakReference<Image>(image), kind, value));
        return image;
    }

    string ResolveKey(string kind, int value)
    {
        if (kind == "nationflag")
        {
            string flagKey = $"graphics/pictures/nation/{value}/flag";
            if (index.ContainsKey(flagKey)) return flagKey;
            string logoKey = $"graphics/pictures/nation/{value}/logo";
            if (index.ContainsKey(logoKey)) return logoKey;
            string iconKey = $"graphics/pictures/nation/{value}/icon";
            if (index.ContainsKey(iconKey)) return iconKey;
            return flagKey;
        }
        if (kind == "nation")
        {
            string logoKey = $"graphics/pictures/nation/{value}/logo";
            if (index.ContainsKey(logoKey)) return logoKey;
            string flagKey = $"graphics/pictures/nation/{value}/flag";
            if (index.ContainsKey(flagKey)) return flagKey;
            return logoKey;
        }
        if (kind == "person")
        {
            return $"graphics/pictures/person/{value}/portrait";
        }
        if (kind == "comp")
        {
            string compKey = $"graphics/pictures/comp/{value}/logo";
            if (index.ContainsKey(compKey)) return compKey;
            string competitionKey = $"graphics/pictures/competition/{value}/logo";
            if (index.ContainsKey(competitionKey)) return competitionKey;
            return compKey;
        }
        if (kind == "award")
        {
            string awardKey = $"graphics/pictures/award/{value}/logo";
            if (index.ContainsKey(awardKey)) return awardKey;
            string trophyKey = $"graphics/pictures/trophy/{value}/logo";
            if (index.ContainsKey(trophyKey)) return trophyKey;
            return awardKey;
        }
        if (kind == "stadium")
        {
            string stadiumKey = $"graphics/pictures/stadium/{value}/logo";
            if (index.ContainsKey(stadiumKey)) return stadiumKey;
            string pitchKey = $"graphics/pictures/stadium/{value}/pitch";
            if (index.ContainsKey(pitchKey)) return pitchKey;
            return stadiumKey;
        }
        return $"graphics/pictures/{kind}/{value}/logo";
    }

    Bitmap? Resolve(string key)
    {
        if (bitmaps.TryGetValue(key, out var cached)) return cached;
        if (!index.TryGetValue(key, out var candidates)) return null;
        foreach (var candidate in candidates)
        foreach (var path in Path.HasExtension(candidate) ? new[] { candidate } : new[] { candidate + ".png", candidate + ".jpg", candidate + ".jpeg" })
        {
            try { if (!File.Exists(path)) continue; using var stream = File.OpenRead(path); return bitmaps[key] = new Bitmap(stream); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }
}
