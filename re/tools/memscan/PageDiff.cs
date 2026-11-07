using System.Security.Cryptography;

// RE helper: hash every 4 KB page of the game's heap, then list the bytes that changed after one action in FMRTE.
static partial class Program
{
    static void PageSnap(string file)
    {
        using var w = new BinaryWriter(File.Create(file));
        long pages = 0;
        foreach (var (baseAddr, d) in Regions(false))
            for (int o = 0; o + 4096 <= d.Length; o += 4096)
            {
                w.Write(baseAddr + (ulong)o);
                w.Write(MD5.HashData(d.AsSpan(o, 4096)));
                pages++;
            }
        Console.WriteLine($"{pages} pages hashed to {file}");
    }

    // Page contents are not stored, so the snapshot cannot say which bytes changed; pagediff therefore needs the
    // pages it reports to be dumped by a second pagesnap-with-copy. Run: pagesnap before, change, pagediff after, which
    // prints the pages that changed and a hex dump of each (current content) so the changed offsets can be compared by eye
    // against the value that was typed.
    static void PageDiff(string file, int maxPages)
    {
        var before = new Dictionary<ulong, byte[]>();
        using (var r = new BinaryReader(File.OpenRead(file)))
            while (r.BaseStream.Position < r.BaseStream.Length) before[r.ReadUInt64()] = r.ReadBytes(16);
        int changed = 0;
        foreach (var (baseAddr, d) in Regions(false))
            for (int o = 0; o + 4096 <= d.Length; o += 4096)
            {
                ulong addr = baseAddr + (ulong)o;
                if (!before.TryGetValue(addr, out var h) || h.AsSpan().SequenceEqual(MD5.HashData(d.AsSpan(o, 4096)))) continue;
                if (++changed > maxPages) continue;
                Console.WriteLine($"changed page 0x{addr:X}");
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"page_{addr:X}.bin"), d.AsSpan(o, 4096).ToArray());
            }
        Console.WriteLine($"{changed} changed pages (first {Math.Min(changed, maxPages)} saved to the temp folder as page_ADDR.bin)");
    }
}
