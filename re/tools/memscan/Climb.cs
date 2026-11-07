using System.Text;

// RE helper: climb upwards through pointers from a set of leaf addresses (a table row, a rules record...) and report
// the shortest chain whose containing object also holds a pointer to a chosen goal object (the competition).
//   memscan climb <leafHex,leafHex,...> <goalHex> <levels>
static partial class Program
{
    static void ClimbUp(string leavesCsv, ulong goal, int levels)
    {
        var frontier = new List<(ulong Node, string Path)>();
        foreach (var leaf in leavesCsv.Split(','))
            if (ulong.TryParse(leaf.Trim().TrimStart('0', 'x', 'X'), System.Globalization.NumberStyles.HexNumber, null, out ulong value))
                frontier.Add((value, $"leaf 0x{value:X}"));
        const ulong MinHeap = 0x10000000000, MaxHeap = 0x7FF000000000;
        for (int level = 1; level <= levels && frontier.Count > 0; level++)
        {
            var ranges = frontier.Select(f => (Lo: f.Node > 0x100 ? f.Node - 0x100 : 0UL, Hi: f.Node + 0x800, f.Node, f.Path))
                .OrderBy(r => r.Lo).ToArray();
            var next = new Dictionary<ulong, string>();
            var goalHits = new List<ulong>();
            foreach (var (baseAddr, d) in Regions(false))
            {
                for (int o = 0; o + 8 <= d.Length; o += 8)
                {
                    ulong v = BitConverter.ToUInt64(d, o);
                    if (v < MinHeap || v >= MaxHeap) continue;
                    if (v == goal) { goalHits.Add(baseAddr + (ulong)o); continue; }
                    int lo = 0, hi = ranges.Length - 1, found = -1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (ranges[mid].Lo <= v) { found = mid; lo = mid + 1; } else hi = mid - 1;
                    }
                    if (found < 0 || v > ranges[found].Hi) continue;
                    ulong source = baseAddr + (ulong)o;
                    if (!next.ContainsKey(source)) next[source] = $"{ranges[found].Path} <- 0x{source:X}";
                }
            }
            Console.WriteLine($"level {level}: {next.Count} candidate holders, {goalHits.Count} goal pointers");
            // A candidate is interesting when a goal pointer sits inside its first 0x800 bytes.
            foreach (var (source, path) in next)
            {
                bool linked = goalHits.Any(g => g >= source && g < source + 0x800);
                if (!linked) continue;
                Console.WriteLine($"CHAIN {path}");
                return;
            }
            if (next.Count > 200000) { Console.WriteLine("too many candidates, stopping"); return; }
            frontier = next.Select(kv => (kv.Key, kv.Value)).ToList();
        }
        Console.WriteLine("no chain found");
    }
}
