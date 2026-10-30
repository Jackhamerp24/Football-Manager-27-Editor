// Read-only memory scanner for Football Manager (fm.exe). Opens the process with
// PROCESS_QUERY_INFORMATION | PROCESS_VM_READ only; never writes.
//
//   memscan players [attrStart attrEnd]   find Player-like objects, histogram by vtable
//   memscan dump <hexAddr> <len>          hex dump
//   memscan name <text>                   find UTF-16/UTF-8 occurrences of a string
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

static class Native
{
    public const uint QueryInfo = 0x0400, VmRead = 0x0010;
    [DllImport("kernel32", SetLastError = true)] public static extern nint OpenProcess(uint access, bool inherit, int pid);
    public const uint VmWrite = 0x0020, VmOperation = 0x0008;
    [DllImport("kernel32", SetLastError = true)] public static extern bool WriteProcessMemory(nint h, nint addr, byte[] buf, nint size, out nint written);
    [DllImport("kernel32", SetLastError = true)] public static extern bool ReadProcessMemory(nint h, nint addr, byte[] buf, nint size, out nint read);
    [DllImport("kernel32", SetLastError = true)] public static extern nint VirtualQueryEx(nint h, nint addr, out MBI info, nint size);
    [StructLayout(LayoutKind.Sequential)]
    public struct MBI { public nint BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public nint RegionSize; public uint State, Protect, Type; }
}

static class Program
{
    static nint H;
    static ulong ModBase, ModEnd;

    static int Main(string[] args)
    {
        var proc = Process.GetProcessesByName("fm").FirstOrDefault() ?? throw new Exception("fm.exe not running");
        H = Native.OpenProcess(Native.QueryInfo | Native.VmRead, false, proc.Id);
        if (H == 0) throw new Exception("OpenProcess failed: " + Marshal.GetLastWin32Error());
        var gp = proc.Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("game_plugin.dll", StringComparison.OrdinalIgnoreCase));
        ModBase = (ulong)gp.BaseAddress; ModEnd = ModBase + (ulong)gp.ModuleMemorySize;
        Console.WriteLine($"pid={proc.Id} game_plugin=0x{ModBase:X}-0x{ModEnd:X}");

        switch (args[0])
        {
            case "players": Players(args.Length > 2 ? Convert.ToInt32(args[1], 16) : 0xA7, args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0xDC); break;
            case "layout": Layout(args.Length > 1 ? int.Parse(args[1]) : 54); break;
            case "match": Match(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16), args.Skip(3).ToArray()); break;
            case "write": WriteAttr(proc.Id, Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16), int.Parse(args[3]), int.Parse(args[4])); break;
            case "arrays": Arrays(Convert.ToUInt64(args[1], 16), int.Parse(args[2])); break;
            case "refs": Refs(Convert.ToInt32(args[1], 16), args.Skip(2).Select(s => Convert.ToUInt64(s, 16)).ToArray()); break;
            case "stats": Stats(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)); break;
            case "walk": Walk(Convert.ToUInt64(args[1], 16)); break;
            case "datesnap": DateSnap(args[1]); break;
            case "datediff": DateDiff(args[1], int.Parse(args[2])); break;
            case "dateroot": DateRoot(args[1], Convert.ToInt32(args[2], 16)); break;
            case "findval": FindValue(Convert.ToUInt64(args[1], 16), int.Parse(args[2]), args.Skip(3).ToArray()); break;
            case "path": PathTo(Convert.ToUInt64(args[1], 16), args[2], int.Parse(args[3])); break;
            case "bytestats": ByteStats(Convert.ToUInt64(args[1], 16), int.Parse(args[2]), int.Parse(args[3])); break;
            case "field": FindField(args.Skip(1).ToArray()); break;
            case "dump": Dump(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2])); break;
            case "name": FindText(string.Join(" ", args.Skip(1))); break;
        }
        return 0;
    }

    // Private RW heap regions; with includeModule also game_plugin.dll's writable image pages (static data).
    static IEnumerable<(ulong addr, byte[] data)> Regions(bool includeModule = false)
    {
        nint a = 0;
        while (Native.VirtualQueryEx(H, a, out var m, Marshal.SizeOf<Native.MBI>()) != 0)
        {
            const uint Commit = 0x1000, Private = 0x20000, Image = 0x1000000, RW = 0x04, WriteCopy = 0x08;
            uint prot = m.Protect & 0xFF;
            bool heap = m.Type == Private && prot == RW;
            bool modData = includeModule && m.Type == Image && (prot == RW || prot == WriteCopy)
                           && (ulong)m.BaseAddress >= ModBase && (ulong)m.BaseAddress < ModEnd;
            if (m.State == Commit && (heap || modData) && m.RegionSize < (nint)(1L << 31))
            {
                var buf = new byte[(long)m.RegionSize];
                if (Native.ReadProcessMemory(H, m.BaseAddress, buf, m.RegionSize, out var read) && read > 0)
                    yield return ((ulong)m.BaseAddress, buf);
            }
            a = m.BaseAddress + m.RegionSize;
            if (a <= 0) break;
        }
    }

    // A Player candidate: vtable at +0 inside game_plugin.dll and every byte of
    // [attrStart, attrEnd] in 1..100 (FM stores 1-20 attributes on a 1-100 scale).
    static void Players(int attrStart, int attrEnd)
    {
        var hist = new Dictionary<ulong, List<ulong>>();
        long scanned = 0;
        foreach (var (baseAddr, d) in Regions())
        {
            scanned += d.Length;
            for (int o = 0; o + 0x200 <= d.Length; o += 8)
            {
                ulong vt = BitConverter.ToUInt64(d, o);
                if (vt < ModBase || vt >= ModEnd) continue;
                bool ok = true;
                for (int k = attrStart; k <= attrEnd; k++) { byte b = d[o + k]; if (b < 1 || b > 100) { ok = false; break; } }
                if (!ok) continue;
                if (!hist.TryGetValue(vt, out var l)) hist[vt] = l = new List<ulong>();
                l.Add(baseAddr + (ulong)o);
            }
        }
        Console.WriteLine($"scanned {scanned / (1 << 20)} MB, attr window 0x{attrStart:X}-0x{attrEnd:X}");
        foreach (var kv in hist.OrderByDescending(k => k.Value.Count).Take(10))
            Console.WriteLine($"vtable 0x{kv.Key:X} (rva 0x{kv.Key - ModBase:X}) count={kv.Value.Count} sample=0x{kv.Value[0]:X}");
    }

    // Offset-agnostic: for every object with a game_plugin vtable at +0, find the first
    // offset in [0x40, 0x400) that starts a run of `minRun` bytes all in 1..100.
    // Histogram (vtable, offset) to reveal where the attribute block moved to.
    static void Layout(int minRun)
    {
        var hist = new Dictionary<(ulong vt, int off), (int n, ulong sample)>();
        foreach (var (baseAddr, d) in Regions())
        {
            // run[i] = number of consecutive in-range bytes starting at i
            var run = new ushort[d.Length + 1];
            for (int i = d.Length - 1; i >= 0; i--)
                run[i] = (ushort)(d[i] >= 1 && d[i] <= 100 ? Math.Min(run[i + 1] + 1, 60000) : 0);
            for (int o = 0; o + 0x400 + minRun <= d.Length; o += 8)
            {
                ulong vt = BitConverter.ToUInt64(d, o);
                if (vt < ModBase || vt >= ModEnd) continue;
                for (int k = 0x40; k < 0x400; k++)
                    if (run[o + k] >= minRun && run[o + k] < minRun + 24)
                    {
                        var key = (vt, k);
                        hist[key] = hist.TryGetValue(key, out var v) ? (v.n + 1, v.sample) : (1, baseAddr + (ulong)o);
                        break;
                    }
            }
        }
        foreach (var kv in hist.OrderByDescending(k => k.Value.n).Take(15))
            Console.WriteLine($"vtable rva 0x{kv.Key.vt - ModBase:X} attrOff 0x{kv.Key.off:X} count={kv.Value.n} sample=0x{kv.Value.sample:X}");
    }

    // Finds objects with vtable rva `vtRva` whose displayed attributes (round(byte/5))
    // equal the given "fm26Offset=value" pairs, read at object + baseDelta + fm26Offset.
    static void Match(ulong vtRva, int baseDelta, string[] pairs)
    {
        var want = pairs.Select(p => p.Split('=')).Select(a => (off: Convert.ToInt32(a[0], 16), val: int.Parse(a[1]))).ToArray();
        ulong vt = ModBase + vtRva;
        int total = 0;
        var best = new List<(int miss, ulong addr, string detail)>();
        foreach (var (baseAddr, d) in Regions())
            for (int o = 0; o + 0x400 <= d.Length; o += 8)
            {
                if (BitConverter.ToUInt64(d, o) != vt) continue;
                total++;
                // Off by at most 1 is tolerated as a rounding ambiguity (raw values like 62/63).
                var bad = want.Where(w => Math.Abs(d[o + baseDelta + w.off] / 5.0 - w.val) > 0.6).ToList();
                if (bad.Count <= want.Length / 3)
                    best.Add((bad.Count, baseAddr + (ulong)o,
                        string.Join(" ", bad.Select(w => $"{w.off:X}:want {w.val} raw {d[o + baseDelta + w.off]}"))));
            }
        foreach (var b in best.OrderBy(b => b.miss).Take(5))
            Console.WriteLine($"0x{b.addr:X} miss={b.miss}/{want.Length} {b.detail}");
        Console.WriteLine($"objects={total} candidates={best.Count}");
    }

    // The only mode that writes: one attribute byte of one verified Player object.
    // Refuses unless the object's vtable is the FM27 Player vtable and the current
    // displayed value equals `expectOld`. Writes display*5 and reads it back.
    const ulong PlayerVtRva = 0x4D27228;
    const int Fm27Delta = 0xB8;
    static void WriteAttr(int pid, ulong obj, int fm26Off, int expectOld, int newDisplay)
    {
        if (fm26Off < 0xA7 || fm26Off > 0xDC || newDisplay < 1 || newDisplay > 20) throw new ArgumentException("offset or value out of range");
        if (BitConverter.ToUInt64(Read(obj, 8), 0) != ModBase + PlayerVtRva) throw new Exception("not a Player object (vtable mismatch)");
        ulong addr = obj + (ulong)(Fm27Delta + fm26Off);
        byte old = Read(addr, 1)[0];
        int oldDisplay = (int)Math.Round(old / 5.0, MidpointRounding.AwayFromZero);
        if (oldDisplay != expectOld) throw new Exception($"current value {oldDisplay} (raw {old}) != expected {expectOld}; nothing written");
        var hw = Native.OpenProcess(Native.QueryInfo | Native.VmRead | Native.VmWrite | Native.VmOperation, false, pid);
        if (hw == 0) throw new Exception("OpenProcess(write) failed: " + Marshal.GetLastWin32Error());
        var nb = new[] { (byte)(newDisplay * 5) };
        if (!Native.WriteProcessMemory(hw, (nint)addr, nb, 1, out var w) || w != 1) throw new Exception("WriteProcessMemory failed: " + Marshal.GetLastWin32Error());
        byte back = Read(addr, 1)[0];
        Console.WriteLine($"0x{addr:X}: raw {old} ({oldDisplay}) -> {back} ({back / 5}); undo: write {obj:X} {fm26Off:X} {newDisplay} {oldDisplay} (restores raw {newDisplay * 5}->{oldDisplay * 5}, original raw was {old})");
    }

    // Static root: qword [rva+0x10] = first entry, [rva+0x18] = end of the pointer array.
    // Histograms the vtables of the referenced objects.
    static void Walk(ulong rootRva)
    {
        var root = Read(ModBase + rootRva, 0x20);
        ulong begin = BitConverter.ToUInt64(root, 0x10), end = BitConverter.ToUInt64(root, 0x18);
        int n = (int)((end - begin) / 8);
        Console.WriteLine($"root game_plugin+0x{rootRva:X}: begin=0x{begin:X} end=0x{end:X} entries={n}");
        var ptrs = Read(begin, n * 8);
        var hist = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            ulong p = BitConverter.ToUInt64(ptrs, i * 8);
            ulong vt = p == 0 ? 0 : BitConverter.ToUInt64(Read(p, 8), 0);
            string key = vt >= ModBase && vt < ModEnd ? $"vt+0x{vt - ModBase:X}" : (p == 0 ? "null" : "other");
            hist[key] = hist.GetValueOrDefault(key) + 1;
        }
        foreach (var kv in hist.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Key} {kv.Value}");
    }

    // Breadth-first over heap pointers inside each node's first 0x400 bytes, printing every
    // pointer chain whose target (or target+4/+8, length-prefixed strings) holds `text`.
    static void PathTo(ulong start, string text, int maxDepth)
    {
        var needle = Encoding.UTF8.GetBytes(text);
        var seen = new HashSet<ulong> { start };
        var frontier = new List<(ulong node, string path)> { (start, "obj") };
        for (int depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<(ulong, string)>();
            foreach (var (node, path) in frontier)
            {
                var b = Read(node, 0x400);
                for (int o = 0; o + 8 <= b.Length; o += 8)
                {
                    ulong p = BitConverter.ToUInt64(b, o);
                    if (p < 0x10000000000 || p > 0x7FF000000000 || (p >= ModBase && p < ModEnd)) continue;
                    var t = Read(p, 0x40);
                    int hit = t.AsSpan().IndexOf(needle);
                    if (hit >= 0 && hit <= 0x10) Console.WriteLine($"{path}+0x{o:X} -> 0x{p:X} (text at +0x{hit:X})");
                    if (seen.Add(p)) next.Add((p, $"{path}+0x{o:X}->"));
                }
            }
            frontier = next.Count > 20000 ? next.Take(20000).ToList() : next;
        }
    }

    // "objHex=value" pairs (value may be "a/b" for a two-int16 pair, e.g. dayOfYear/year).
    // Prints object offsets where every object holds its value as byte, int16 or int32.
    static void FindField(string[] pairs)
    {
        var items = pairs.Select(p => p.Split('=')).Select(a => (obj: Convert.ToUInt64(a[0], 16), val: a[1])).ToArray();
        var data = items.Select(i => Read(i.obj, 0x3C8)).ToArray();
        for (int off = 0; off < 0x3C8 - 4; off++)
        {
            string kind = null;
            bool all8 = true, all16 = off % 2 == 0, all32 = off % 4 == 0, allPair = off % 2 == 0;
            for (int i = 0; i < items.Length; i++)
            {
                var d = data[i]; var v = items[i].val;
                if (v.Contains('/'))
                {
                    var ab = v.Split('/').Select(int.Parse).ToArray();
                    allPair &= BitConverter.ToInt16(d, off) == ab[0] && BitConverter.ToInt16(d, off + 2) == ab[1];
                    all8 = all16 = all32 = false;
                }
                else
                {
                    int x = int.Parse(v);
                    all8 &= d[off] == x; all16 &= BitConverter.ToInt16(d, off) == x; all32 &= BitConverter.ToInt32(d, off) == x;
                    allPair = false;
                }
            }
            kind = allPair ? "int16 pair" : all32 ? "int32" : all16 ? "int16" : all8 ? "byte" : null;
            if (kind != null) Console.WriteLine($"+0x{off:X3} {kind}");
        }
    }

    // Byte offsets whose value is in [lo, hi] for >= 99% of objects and that vary across objects.
    static void ByteStats(ulong vtRva, int lo, int hi)
    {
        var objs = ObjectsWithVtable(vtRva);
        var ok = new int[0x3C8]; var distinct = new HashSet<byte>[0x3C8];
        for (int k = 0; k < 0x3C8; k++) distinct[k] = new HashSet<byte>();
        foreach (var o in objs)
        {
            var b = Read(o, 0x3C8);
            for (int k = 0; k < 0x3C8; k++) if (b[k] >= lo && b[k] <= hi) { ok[k]++; distinct[k].Add(b[k]); }
        }
        Console.WriteLine($"objects={objs.Length}");
        for (int k = 0; k < 0x3C8; k++)
            if (ok[k] >= objs.Length * 0.99 && distinct[k].Count > hi / 2) Console.Write($"+0x{k:X3} ");
        Console.WriteLine();
    }

    // Breadth-first over heap pointers (first 0x400 bytes of each node, depth <= maxDepth) and
    // reports where a value appears: "i32:135000", "i16:7382", "date:181/2031" (int16 day, int16 year), "text:Man Utd".
    static void FindValue(ulong start, int maxDepth, string[] wants)
    {
        var pats = wants.Select(w =>
        {
            var kv = w.Split(':', 2);
            byte[] p = kv[0] switch
            {
                "i32" => BitConverter.GetBytes(int.Parse(kv[1])),
                "i16" => BitConverter.GetBytes(short.Parse(kv[1])),
                "date" => kv[1].Split('/').Select(short.Parse).SelectMany(BitConverter.GetBytes).ToArray(),
                "text" => Encoding.UTF8.GetBytes(kv[1]),
                _ => throw new ArgumentException(w),
            };
            return (w, p);
        }).ToArray();
        var seen = new HashSet<ulong> { start };
        var frontier = new List<(ulong node, string path)> { (start, "obj") };
        for (int depth = 0; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<(ulong, string)>();
            foreach (var (node, path) in frontier)
            {
                var b = Read(node, 0x400);
                foreach (var (w, p) in pats)
                    for (int i = b.AsSpan().IndexOf(p), off = 0; i >= 0; i = b.AsSpan(off).IndexOf(p))
                    {
                        Console.WriteLine($"{w} at {path}+0x{off + i:X}  (node 0x{node:X})");
                        off += i + 1;
                    }
                if (depth == maxDepth) continue;
                for (int o = 0; o + 8 <= b.Length; o += 8)
                {
                    ulong q = BitConverter.ToUInt64(b, o);
                    if (q < 0x10000000000 || q > 0x7FF000000000 || (q >= ModBase && q < ModEnd)) continue;
                    if (seen.Add(q)) next.Add((q, $"{path}+0x{o:X}->"));
                }
            }
            frontier = next.Count > 30000 ? next.Take(30000).ToList() : next;
        }
    }

    // Snapshot of every int16 pair (day 1..366, year 2026..2027) at even addresses in heap and
    // game_plugin static data. Saved as (address u64, day i16, year i16).
    static void DateSnap(string file)
    {
        using var w = new BinaryWriter(File.Create(file));
        long n = 0;
        foreach (var (baseAddr, d) in Regions(true))
            for (int o = 0; o + 4 <= d.Length; o += 2)
            {
                short year = BitConverter.ToInt16(d, o + 2);
                if (year != 2026 && year != 2027) continue;
                short day = BitConverter.ToInt16(d, o);
                if (day < 1 || day > 366) continue;
                w.Write(baseAddr + (ulong)o); w.Write(day); w.Write(year); n++;
            }
        Console.WriteLine($"{n} date pairs saved to {file}");
    }

    // Re-reads every snapshot location; prints those that moved forward by exactly `days`.
    static void DateDiff(string file, int days)
    {
        using var r = new BinaryReader(File.OpenRead(file));
        var hits = new List<(ulong addr, DateOnly before, DateOnly after)>();
        var buf = new byte[4];
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            ulong a = r.ReadUInt64(); short day = r.ReadInt16(), year = r.ReadInt16();
            if (!Native.ReadProcessMemory(H, (nint)a, buf, 4, out var read) || read != 4) continue;
            short day2 = BitConverter.ToInt16(buf, 0), year2 = BitConverter.ToInt16(buf, 2);
            if (year2 < 2026 || year2 > 2027 || day2 < 1 || day2 > 366) continue;
            var before = new DateOnly(year, 1, 1).AddDays(day - 1);
            var after = new DateOnly(year2, 1, 1).AddDays(day2 - 1);
            if (after.DayNumber - before.DayNumber == days) hits.Add((a, before, after));
        }
        Console.WriteLine($"{hits.Count} locations advanced by {days} day(s)");
        foreach (var h in hits.OrderBy(h => h.addr >= ModBase && h.addr < ModEnd ? 0 : 1).Take(40))
            Console.WriteLine($"{Where(h.addr)}  {h.before:yyyy-MM-dd} -> {h.after:yyyy-MM-dd}");
    }

    // Locations that advanced by one day to the latest date, outside player objects, and the
    // game_plugin static qwords pointing at most `tol` bytes before them (object base + field).
    static void DateRoot(string file, int tol)
    {
        var players = ObjectsWithVtable(0x4D27228).Concat(ObjectsWithVtable(0x4F75A08)).OrderBy(x => x).ToArray();
        bool InPlayer(ulong a)
        {
            int i = Array.BinarySearch(players, a);
            if (i < 0) i = ~i - 1;
            return i >= 0 && a - players[i] < 0x3C8;
        }
        using var r = new BinaryReader(File.OpenRead(file));
        var hits = new List<(ulong a, DateOnly after)>();
        var buf = new byte[4];
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            ulong a = r.ReadUInt64(); short day = r.ReadInt16(), year = r.ReadInt16();
            if (!Native.ReadProcessMemory(H, (nint)a, buf, 4, out var read) || read != 4) continue;
            short d2 = BitConverter.ToInt16(buf, 0), y2 = BitConverter.ToInt16(buf, 2);
            if (y2 < 2026 || y2 > 2027 || d2 < 1 || d2 > 366) continue;
            var before = new DateOnly(year, 1, 1).AddDays(day - 1);
            var after = new DateOnly(y2, 1, 1).AddDays(d2 - 1);
            if (after.DayNumber - before.DayNumber == 1) hits.Add((a, after));
        }
        // The current date is the one most locations advanced to (player objects carry it too).
        var today = hits.GroupBy(h => h.after).MaxBy(g => g.Count())!.Key;
        var targets = hits.Where(h => h.after == today && !InPlayer(h.a)).Select(h => h.a).OrderBy(x => x).ToArray();
        Console.WriteLine($"current date {today:yyyy-MM-dd}: {targets.Length} locations outside player objects");
        foreach (var t in targets.Take(30)) Console.WriteLine($"  {Where(t)}");
        int? Hit(ulong q)
        {
            int i = Array.BinarySearch(targets, q);
            if (i < 0) i = ~i;
            return i < targets.Length && targets[i] - q <= (ulong)tol ? (int)(targets[i] - q) : null;
        }
        Console.WriteLine("static -> date and static -> object -> date:");
        var node = new byte[0x400];
        foreach (var (baseAddr, d) in Regions(true))
        {
            if (baseAddr < ModBase || baseAddr >= ModEnd) continue;
            for (int o = 0; o + 8 <= d.Length; o += 8)
            {
                ulong q = BitConverter.ToUInt64(d, o);
                if (q < 0x10000000000 || q > 0x7FF000000000 || (q >= ModBase && q < ModEnd)) continue;
                string root = $"game_plugin+0x{baseAddr + (ulong)o - ModBase:X}";
                if (Hit(q) is { } h1) Console.WriteLine($"  {root} -> +0x{h1:X}");
                if (!Native.ReadProcessMemory(H, (nint)q, node, node.Length, out var read) || read != node.Length) continue;
                for (int k = 0; k + 8 <= node.Length; k += 8)
                    if (Hit(BitConverter.ToUInt64(node, k)) is { } h2)
                        Console.WriteLine($"  {root} -> +0x{k:X} -> +0x{h2:X}");
            }
        }
    }

    static ulong[] ObjectsWithVtable(ulong vtRva)
    {
        ulong vt = ModBase + vtRva;
        var list = new List<ulong>();
        foreach (var (baseAddr, d) in Regions())
            for (int o = 0; o + 8 <= d.Length; o += 8)
                if (BitConverter.ToUInt64(d, o) == vt) list.Add(baseAddr + (ulong)o);
        return list.ToArray();
    }

    static string Where(ulong a) => a >= ModBase && a < ModEnd ? $"game_plugin+0x{a - ModBase:X}" : $"heap 0x{a:X}";

    // Pointer arrays: runs of consecutive qwords that each point at an object with the vtable.
    static void Arrays(ulong vtRva, int minRun)
    {
        var objs = new HashSet<ulong>(ObjectsWithVtable(vtRva));
        Console.WriteLine($"objects: {objs.Count}");
        foreach (var (baseAddr, d) in Regions(true))
        {
            int run = 0, start = 0;
            for (int o = 0; o + 8 <= d.Length; o += 8)
            {
                if (objs.Contains(BitConverter.ToUInt64(d, o))) { if (run++ == 0) start = o; }
                else
                {
                    if (run >= minRun) Console.WriteLine($"array {Where(baseAddr + (ulong)start)} count={run}");
                    run = 0;
                }
            }
            if (run >= minRun) Console.WriteLine($"array {Where(baseAddr + (ulong)start)} count={run}");
        }
    }

    // Every qword (heap or game_plugin data) pointing into [target - tol, target].
    static void Refs(int tol, ulong[] targets)
    {
        Array.Sort(targets);
        int hits = 0;
        foreach (var (baseAddr, d) in Regions(true))
            for (int o = 0; o + 8 <= d.Length; o += 8)
            {
                ulong q = BitConverter.ToUInt64(d, o);
                int i = Array.BinarySearch(targets, q);
                if (i < 0) i = ~i;               // first target >= q
                if (i >= targets.Length || targets[i] - q > (ulong)tol) continue;
                ulong loc = baseAddr + (ulong)o;
                string ctx = string.Join(" ", Enumerable.Range(1, 3).Select(k => o + 8 * k + 8 <= d.Length ? $"{BitConverter.ToUInt64(d, o + 8 * k):X}" : "-"));
                Console.WriteLine($"{Where(loc)} -> 0x{q:X} (target 0x{targets[i]:X} -{targets[i] - q:X})  next: {ctx}");
                if (++hits >= 60) return;
            }
        Console.WriteLine($"hits={hits}");
    }

    // Per-offset int16 statistics over all objects with the vtable: share in 1..200,
    // and for (off, off+2) pairs the share where second >= first (CA <= PA).
    static void Stats(ulong vtRva, int size)
    {
        var objs = ObjectsWithVtable(vtRva);
        var inRange = new int[size / 2]; var ordered = new int[size / 2];
        var sum = new long[size / 2];
        foreach (var obj in objs)
        {
            var b = Read(obj, size);
            for (int k = 0; k + 4 <= size; k += 2)
            {
                short x = BitConverter.ToInt16(b, k), y = BitConverter.ToInt16(b, k + 2);
                if (x >= 1 && x <= 200) { inRange[k / 2]++; sum[k / 2] += x; if (y >= x && y <= 200) ordered[k / 2]++; }
            }
        }
        Console.WriteLine($"objects={objs.Length}");
        for (int k = 0; k < size / 2; k++)
            if (inRange[k] > objs.Length * 0.9)
                Console.WriteLine($"+0x{k * 2:X3} in1..200={100.0 * inRange[k] / objs.Length:F1}% avg={(double)sum[k] / inRange[k]:F1} next>=this={100.0 * ordered[k] / objs.Length:F1}%");
    }

    static byte[] Read(ulong addr, int len)
    {
        var b = new byte[len];
        Native.ReadProcessMemory(H, (nint)addr, b, len, out _);
        return b;
    }

    static void Dump(ulong addr, int len)
    {
        var b = Read(addr, len);
        for (int i = 0; i < len; i += 16)
            Console.WriteLine($"+{i:X3}  {BitConverter.ToString(b, i, Math.Min(16, len - i)).Replace('-', ' ')}");
    }

    static void FindText(string s)
    {
        var pats = new[] { ("utf16", Encoding.Unicode.GetBytes(s)), ("utf8", Encoding.UTF8.GetBytes(s)) };
        int hits = 0;
        foreach (var (baseAddr, d) in Regions())
            foreach (var (enc, p) in pats)
                for (int i = d.AsSpan().IndexOf(p), off = 0; i >= 0 && hits < 50; )
                {
                    Console.WriteLine($"{enc} 0x{baseAddr + (ulong)(off + i):X}");
                    hits++; off += i + 1; i = d.AsSpan(off).IndexOf(p);
                }
    }
}
