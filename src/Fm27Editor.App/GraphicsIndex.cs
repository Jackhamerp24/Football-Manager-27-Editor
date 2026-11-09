using System.Text;
using System.Xml;

namespace Fm27Editor.App;

/// <summary>
/// Maps FM graphics keys (graphics/pictures/club/680/logo...) to image files under a graphics folder, read from each config.xml.
/// A full pack has over a million records, so the index keeps no strings per key: each key is a 64-bit hash (sorted, binary
/// searched) and each path is a slice of one UTF-8 pool, relative to the folder. About 16 bytes plus the path per record.
/// A hash collision would map a key to another key's file; with ~10^6 keys that chance is about 3 in 10^8.
/// </summary>
public sealed class GraphicsIndex
{
    public static readonly GraphicsIndex Empty = new(string.Empty, [], [], [], []);

    readonly string root;
    readonly ulong[] keys;     // sorted by key hash; equal hashes keep config order
    readonly int[] starts;     // offset of each record's relative path in pool, aligned with keys
    readonly int[] lengths;    // byte length of each record's relative path
    readonly byte[] pool;

    GraphicsIndex(string root, ulong[] keys, int[] starts, int[] lengths, byte[] pool)
    {
        this.root = root; this.keys = keys; this.starts = starts; this.lengths = lengths; this.pool = pool;
    }

    public int Count => keys.Length;

    public bool Contains(string key)
    {
        ulong h = Hash(key);
        int i = LowerBound(h);
        return i < keys.Length && keys[i] == h;
    }

    /// <summary>Full paths of every file the config files map to <paramref name="key"/>, in config order.</summary>
    public IEnumerable<string> Paths(string key)
    {
        ulong h = Hash(key);
        for (int i = LowerBound(h); i < keys.Length && keys[i] == h; i++)
            yield return Path.Combine(root, Encoding.UTF8.GetString(pool, starts[i], lengths[i]));
    }

    int LowerBound(ulong h)
    {
        int lo = 0, hi = keys.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (keys[mid] < h) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>FNV-1a over the lower-cased key: the keys are compared ignoring case, as the old dictionary did.</summary>
    static ulong Hash(ReadOnlySpan<char> key)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in key)
        {
            h ^= char.ToLowerInvariant(c);
            h *= 1099511628211UL;
        }
        return h;
    }

    public static GraphicsIndex Scan(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return Empty;
        string root = Path.GetFullPath(folder);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var hashes = new List<ulong>();
        var starts = new List<int>();
        var lengths = new List<int>();
        using var pool = new MemoryStream();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var config in Directory.EnumerateFiles(root, "config.xml", options).OrderBy(p => p.Contains("small", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var directory = Path.GetDirectoryName(config)!;
                    string directoryPrefix = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
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
                        if (!path.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        var relative = Path.GetRelativePath(root, path);
                        var bytes = Encoding.UTF8.GetBytes(relative);
                        hashes.Add(Hash(key));
                        starts.Add((int)pool.Length);
                        lengths.Add(bytes.Length);
                        pool.Write(bytes);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException or NotSupportedException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        int n = hashes.Count;
        var keyArray = hashes.ToArray();
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(keyArray, order);
        // Array.Sort is not stable: restore config order inside each run of equal keys.
        for (int i = 0; i < n;)
        {
            int j = i + 1;
            while (j < n && keyArray[j] == keyArray[i]) j++;
            if (j - i > 1) Array.Sort(order, i, j - i);
            i = j;
        }
        var startArray = new int[n];
        var lengthArray = new int[n];
        var starts0 = starts.ToArray();
        var lengths0 = lengths.ToArray();
        for (int p = 0; p < n; p++)
        {
            startArray[p] = starts0[order[p]];
            lengthArray[p] = lengths0[order[p]];
        }
        return new GraphicsIndex(root, keyArray, startArray, lengthArray, pool.ToArray());
    }
}
