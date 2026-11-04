param(
    [string]$SteamProfile = (Join-Path $PSScriptRoot '..\..\offsets\27.0.0+2493426.json'),
    [string]$SteamModule = 'C:\Program Files (x86)\Steam\steamapps\common\Football Manager 27\fm_Data\Plugins\x86_64\game_plugin.dll',
    [string]$OutputPath = '',
    [string]$ProfileOutput = ''
)
$ErrorActionPreference = 'Stop'
$source = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

public sealed class GpRttiEntry
{
    public string Path = "", Name = "";
    public uint OldRva, Offset, ConstructorOffset;
}
public static class GpRttiMap
{
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryInfo { public IntPtr Base, AllocationBase; public uint AllocationProtect; public ushort Partition; public UIntPtr RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadProcessMemory(IntPtr h, ulong address, byte[] bytes, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError=true)] static extern UIntPtr VirtualQueryEx(IntPtr h, ulong address, out MemoryInfo info, UIntPtr size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    static bool Read(IntPtr h, ulong address, byte[] b) { IntPtr n; return ReadProcessMemory(h, address, b, b.Length, out n) && n.ToInt64() == b.Length; }
    static uint U32(byte[] b, int i) { return BitConverter.ToUInt32(b, i); }
    static ulong U64(byte[] b, int i) { return BitConverter.ToUInt64(b, i); }
    static bool Ptr(ulong p) { return p >= 0x10000UL && p < 0x800000000000UL; }
    public static string[] Run(int pid, ulong module, int size, string build, GpRttiEntry[] entries)
    {
        var output = new List<string>();
        var image = new byte[size];
        var regions = new List<Tuple<int,int,bool>>();
        IntPtr h = OpenProcess(0x410, false, pid);
        if (h == IntPtr.Zero) throw new Exception("Cannot open game read-only");
        try
        {
            ulong cursor = module, end = module + (ulong)size;
            while (cursor < end)
            {
                MemoryInfo mi;
                if (VirtualQueryEx(h, cursor, out mi, (UIntPtr)Marshal.SizeOf(typeof(MemoryInfo))) == UIntPtr.Zero) break;
                ulong start = Math.Max(cursor, (ulong)mi.Base.ToInt64());
                ulong next = Math.Min(end, (ulong)mi.Base.ToInt64() + mi.RegionSize.ToUInt64());
                if (next <= cursor) break;
                uint prot = mi.Protect & 0xff;
                if (mi.State == 0x1000 && (mi.Protect & 0x100) == 0 && prot != 0 && prot != 1)
                {
                    bool writable = prot == 4 || prot == 8 || prot == 0x40 || prot == 0x80;
                    for (ulong pos = start; pos < next;)
                    {
                        int len = (int)Math.Min(1UL << 20, next - pos);
                        var chunk = new byte[len];
                        if (Read(h, pos, chunk))
                        {
                            int off = checked((int)(pos - module));
                            Buffer.BlockCopy(chunk, 0, image, off, len);
                            regions.Add(Tuple.Create(off, len, writable));
                        }
                        pos += (ulong)len;
                    }
                }
                cursor = next;
            }
            var names = new HashSet<string>(entries.Select(x => x.Name));
            var buildBytes = Encoding.ASCII.GetBytes(build);
            foreach (var range in regions)
            {
                int at = image.AsSpan(range.Item1, range.Item2).IndexOf(buildBytes);
                if (at >= 0) output.Add("BUILD\t0x" + (range.Item1 + at).ToString("X") + "\t" + build);
            }
            var types = new Dictionary<uint,string>();
            foreach (var range in regions)
            {
                int stop = Math.Min(size - 256, range.Item1 + range.Item2 - 1);
                for (int i = Math.Max(16, range.Item1); i < stop; i++)
                {
                    if (image[i] != 46 || image[i+1] != 63 || image[i+2] != 65 || (image[i+3] != 86 && image[i+3] != 85)) continue;
                    int n = 4; while (n < 250 && image[i+n] != 0) n++;
                    if (n == 250) continue;
                    string name = Encoding.ASCII.GetString(image, i, n);
                    if (names.Contains(name)) types[(uint)(i - 16)] = name;
                }
            }
            var locators = new Dictionary<uint,List<uint>>();
            foreach (var range in regions)
            {
                int stop = Math.Min(size - 24, range.Item1 + range.Item2 - 24);
                for (int i = (range.Item1 + 3) & ~3; i <= stop; i += 4)
                {
                    if (U32(image, i) != 1) continue;
                    uint type = U32(image, i+12), self = U32(image, i+20);
                    if (self != (uint)i || !types.ContainsKey(type)) continue;
                    foreach (var e in entries.Where(x => x.Name == types[type] && x.Offset == U32(image, i+4) && x.ConstructorOffset == U32(image, i+8)))
                    {
                        if (!locators.ContainsKey(e.OldRva)) locators[e.OldRva] = new List<uint>();
                        if (!locators[e.OldRva].Contains((uint)i)) locators[e.OldRva].Add((uint)i);
                    }
                }
            }
            var reverse = new Dictionary<ulong,List<uint>>();
            foreach (var pair in locators) foreach (uint loc in pair.Value)
            {
                ulong address = module + loc;
                if (!reverse.ContainsKey(address)) reverse[address] = new List<uint>();
                reverse[address].Add(pair.Key);
            }
            var mapped = new Dictionary<uint,HashSet<uint>>();
            foreach (var range in regions)
            {
                int stop = Math.Min(size - 16, range.Item1 + range.Item2 - 16);
                for (int i = (range.Item1 + 7) & ~7; i <= stop; i += 8)
                {
                    List<uint> old;
                    if (!reverse.TryGetValue(U64(image, i), out old)) continue;
                    ulong first = U64(image, i+8);
                    if (first < module || first >= end) continue;
                    foreach (uint key in old) { if (!mapped.ContainsKey(key)) mapped[key] = new HashSet<uint>(); mapped[key].Add((uint)(i+8)); }
                }
            }
            foreach (var e in entries)
            {
                HashSet<uint> choices;
                if (!mapped.TryGetValue(e.OldRva, out choices)) output.Add("MISSING\t" + e.Path + "\t0x" + e.OldRva.ToString("X") + "\t" + e.Name);
                else output.Add((choices.Count == 1 ? "MAP" : "AMBIG") + "\t" + e.Path + "\t0x" + e.OldRva.ToString("X") + "\t" + string.Join(",", choices.OrderBy(x => x).Select(x => "0x" + x.ToString("X"))) + "\t" + e.Name);
            }
            // Staff base vtables are not preceded by MSVC RTTI locators; recover them from the
            // observed Game Pass staff vector by matching each embedded person vtable.
            var personKinds = entries.Where(x => x.Path == "staff.kinds[0].personVtable" || x.Path == "staff.kinds[1].personVtable").ToArray();
            const int staffRoot = 0x5683E08;
            if (staffRoot + 16 <= image.Length)
            {
                ulong begin = U64(image, staffRoot), finish = U64(image, staffRoot+8);
                if (Ptr(begin) && finish >= begin && (finish-begin)%8 == 0 && (finish-begin)/8 <= 200000)
                {
                    var staffPointers = new byte[(int)(finish-begin)];
                    if (Read(h, begin, staffPointers))
                    {
                        output.Add("DIRECT\tstaff\t0x5683E08\t" + (staffPointers.Length/8));
                        var found = new HashSet<string>(); var foundKinds = new HashSet<ulong>(); var q = new byte[8]; var person = new byte[24];
                        for (int i = 0; i < staffPointers.Length; i += 8)
                        {
                            ulong staff = U64(staffPointers, i);
                            if (!Ptr(staff) || !Read(h, staff, q)) continue;
                            ulong staffVt = U64(q, 0); if (staffVt < module || staffVt >= end) continue;
                            if (!foundKinds.Contains(staffVt))
                                foreach (int personOffset in new[]{0x100, 0x440})
                                {
                                    if (!Read(h, staff + (ulong)personOffset, person)) continue;
                                    ulong personVt = U64(person, 0); int id = BitConverter.ToInt32(person, 12);
                                    if (personVt < module || personVt >= end || id <= 0 || id != BitConverter.ToInt32(person, 16)) continue;
                                    foundKinds.Add(staffVt);
                                    output.Add("STAFFKIND\t0x" + (staffVt-module).ToString("X") + "\t0x" + (personVt-module).ToString("X") + "\t0x" + personOffset.ToString("X"));
                                    break;
                                }
                            foreach (var e in personKinds)
                            {
                                HashSet<uint> choices;
                                if (!mapped.TryGetValue(e.OldRva, out choices) || choices.Count != 1) continue;
                                int offset = e.Path.Contains("[0]") ? 0x100 : 0x440;
                                if (!Read(h, staff + (ulong)offset, q) || U64(q, 0) != module + choices.First()) continue;
                                string key = e.Path.Replace("personVtable", "staffVtable");
                                if (found.Add(key)) output.Add("STAFFVT\t" + key + "\t0x" + (staffVt-module).ToString("X") + "\tperson+0x" + offset.ToString("X"));
                            }
                        }
                    }
                }
            }
            // Static holders are searched once for the known vector offsets used by the Steam layout.
            var targets = new Dictionary<ulong,string>();
            foreach (var e in entries.Where(x => new[]{"nation.vtable","stadium.vtable","competition.vtable","award.vtable","player.cities.vtable","player.proposals.transferVtable","player.proposals.loanVtable"}.Contains(x.Path)))
            {
                HashSet<uint> choices;
                if (mapped.TryGetValue(e.OldRva, out choices) && choices.Count == 1) targets[module + choices.First()] = e.Path;
            }
            var directTargets = new Dictionary<ulong,string>();
            foreach (var e in entries.Where(x => x.Path == "playerVtables[0]" || x.Path == "team.vtable" || x.Path == "player.callUps.vtable"))
            {
                HashSet<uint> choices;
                if (mapped.TryGetValue(e.OldRva, out choices) && choices.Count == 1)
                    directTargets[module + choices.First()] = e.Path == "playerVtables[0]" ? "players" : "teams";
            }
            var sample = new byte[8];
            foreach (var range in regions.Where(x => x.Item3))
            {
                int stop = Math.Min(size - 16, range.Item1 + range.Item2 - 16);
                for (int i = (range.Item1 + 7) & ~7; i <= stop; i += 8)
                {
                    ulong begin = U64(image, i), finish = U64(image, i+8);
                    if (!Ptr(begin) || finish <= begin || (finish-begin)%8 != 0 || (finish-begin)/8 < 10000 || (finish-begin)/8 > 200000 || !Read(h, begin, sample)) continue;
                    ulong first = U64(sample, 0); if (!Ptr(first) || !Read(h, first, sample)) continue;
                    string kind; if (directTargets.TryGetValue(U64(sample, 0), out kind))
                        output.Add("DIRECT\t" + kind + "\t0x" + i.ToString("X") + "\t" + ((finish-begin)/8) + "\t0x" + begin.ToString("X") + "\t0x" + finish.ToString("X"));
                }
            }
            var seen = new HashSet<ulong>();
            var holder = new byte[0x800]; var eight = new byte[8];
            foreach (var range in regions.Where(x => x.Item3))
            {
                int stop = Math.Min(size - 8, range.Item1 + range.Item2 - 8);
                for (int i = (range.Item1 + 7) & ~7; i <= stop; i += 8)
                {
                    ulong p = U64(image, i);
                    if (!Ptr(p) || (p >= module && p < end) || !seen.Add(p) || !Read(h, p, holder)) continue;
                    for (int off = 0; off + 16 <= holder.Length; off += 8)
                    {
                        ulong begin = U64(holder, off), finish = U64(holder, off+8);
                        if (!Ptr(begin) || finish <= begin || (finish-begin)%8 != 0 || (finish-begin)/8 > 200000 || !Read(h, begin, eight)) continue;
                        ulong first = U64(eight, 0); if (!Ptr(first) || !Read(h, first, eight)) continue;
                        string kind; if (!targets.TryGetValue(U64(eight, 0), out kind)) continue;
                        output.Add("ROOT\t" + kind + "\t0x" + i.ToString("X") + "\t0x" + off.ToString("X") + "\t" + ((finish-begin)/8));
                    }
                }
            }
            uint peOffset = U32(image, 0x3C);
            if (peOffset > (uint)size - 12 || U32(image, (int)peOffset) != 0x4550) throw new Exception("Invalid Game Pass module PE header");
            output.Insert(0, "FINGERPRINT\t0x" + size.ToString("X") + "\t0x" + U32(image, (int)peOffset + 8).ToString("X"));
            output.Insert(0, "SUMMARY\tclasses=" + entries.Length + "\ttypeDescriptors=" + types.Count + "\tregions=" + regions.Count);
            return output.ToArray();
        }
        finally { CloseHandle(h); }
    }
}
'@
Add-Type -TypeDefinition $source
$proc = Get-Process fm | Select-Object -First 1
$module = $proc.Modules | Where-Object { $_.ModuleName -eq 'game_plugin.dll' } | Select-Object -First 1
if (!$module) { throw 'game_plugin.dll is not loaded' }
$profile = Get-Content -Raw $SteamProfile | ConvertFrom-Json
$stream = [System.IO.File]::OpenRead($SteamModule)
try {
    $reader = [System.IO.BinaryReader]::new($stream)
    $stream.Position = 0x3C; $pe = $reader.ReadUInt32()
    $stream.Position = $pe + 6; $count = $reader.ReadUInt16()
    $stream.Position = $pe + 20; $optionalSize = $reader.ReadUInt16()
    $stream.Position = $pe + 48; $imageBase = $reader.ReadUInt64()
    $sectionStart = $pe + 24 + $optionalSize
    $sections = @()
    for ($i = 0; $i -lt $count; $i++) {
        $stream.Position = $sectionStart + $i * 40; $null = $reader.ReadBytes(8)
        $null = $reader.ReadUInt32(); $va = $reader.ReadUInt32(); $size = $reader.ReadUInt32(); $raw = $reader.ReadUInt32()
        $sections += [pscustomobject]@{ VA = $va; Raw = $raw; Size = $size }
    }
    function RawOffset([UInt64]$rva) {
        $section = $sections | Where-Object { $rva -ge $_.VA -and $rva -lt ([UInt64]$_.VA + $_.Size) } | Select-Object -First 1
        if (!$section) { return $null }
        return [UInt64]$section.Raw + ($rva - $section.VA)
    }
    $items = [System.Collections.Generic.List[GpRttiEntry]]::new()
    function AddVtable([string]$path, [string]$text) {
        if ($text -notmatch '^0x[0-9A-Fa-f]+$') { return }
        $rva = [Convert]::ToUInt32($text.Substring(2), 16)
        $at = RawOffset ([UInt64]$rva - 8); if ($null -eq $at) { return }
        $stream.Position = $at; $col = $reader.ReadUInt64(); if ($col -lt $imageBase) { return }
        $colRva = $col - $imageBase; $at = RawOffset $colRva; if ($null -eq $at) { return }
        $stream.Position = $at; $sig = $reader.ReadUInt32(); $offset = $reader.ReadUInt32(); $cd = $reader.ReadUInt32(); $type = $reader.ReadUInt32(); $null = $reader.ReadUInt32(); $self = $reader.ReadUInt32()
        if ($sig -ne 1 -or $self -ne $colRva) { return }
        $at = RawOffset ([UInt64]$type + 16); if ($null -eq $at) { return }
        $stream.Position = $at; $name = [System.Collections.Generic.List[byte]]::new()
        while (($ch = $reader.ReadByte()) -ne 0 -and $name.Count -lt 240) { $name.Add($ch) }
        $entry = [GpRttiEntry]::new(); $entry.Path = $path; $entry.Name = [Text.Encoding]::ASCII.GetString($name.ToArray()); $entry.OldRva = $rva; $entry.Offset = $offset; $entry.ConstructorOffset = $cd
        $items.Add($entry)
    }
    function Walk($value, [string]$path) {
        if ($null -eq $value) { return }
        if ($value -is [System.Management.Automation.PSCustomObject]) {
            foreach ($p in $value.PSObject.Properties) {
                $next = if ($path) { $path + '.' + $p.Name } else { $p.Name }
                if ($p.Name -match '(?i)vtable') {
                    if ($p.Value -is [array]) { for ($i = 0; $i -lt $p.Value.Count; $i++) { AddVtable ($next + '[' + $i + ']') ([string]$p.Value[$i]) } }
                    elseif ($p.Value -is [string]) { AddVtable $next $p.Value }
                } else { Walk $p.Value $next }
            }
        } elseif ($value -is [array]) { for ($i = 0; $i -lt $value.Count; $i++) { Walk $value[$i] ($path + '[' + $i + ']') } }
    }
    Walk $profile ''
    $result = [GpRttiMap]::Run($proc.Id, [UInt64]$module.BaseAddress.ToInt64(), $module.ModuleMemorySize, $profile.build, $items.ToArray())
    if ($OutputPath) { [System.IO.File]::WriteAllLines($OutputPath, $result) }
    $result
    if ($ProfileOutput) {
        $lines = @($result | ForEach-Object { ,($_ -split "`t") })
        if (@($lines | Where-Object { $_[0] -eq 'MISSING' -or $_[0] -eq 'AMBIG' }).Count -ne 0) { throw 'Unresolved vtables; profile not written' }
        $map = @{}
        foreach ($line in $lines | Where-Object { $_[0] -eq 'MAP' }) {
            $key = [Convert]::ToUInt32($line[2].Substring(2), 16)
            if ($map.ContainsKey($key) -and $map[$key] -ne $line[3]) { throw "Conflicting vtable mapping $($line[2])" }
            $map[$key] = $line[3]
        }
        foreach ($i in 0, 1) {
            $line = @($lines | Where-Object { $_[0] -eq 'STAFFVT' -and $_[1] -eq "staff.kinds[$i].staffVtable" })
            if ($line.Count -ne 1) { throw "Staff vtable $i not uniquely identified" }
            $old = [Convert]::ToUInt32($profile.staff.kinds[$i].staffVtable.Substring(2), 16)
            $map[$old] = $line[0][2]
        }
        function ReplaceVtables($value) {
            if ($value -is [System.Management.Automation.PSCustomObject]) {
                foreach ($p in $value.PSObject.Properties) {
                    if ($p.Name -match '(?i)vtable') {
                        if ($p.Value -is [array]) {
                            for ($i = 0; $i -lt $p.Value.Count; $i++) {
                                $key = [Convert]::ToUInt32(([string]$p.Value[$i]).Substring(2), 16)
                                if (!$map.ContainsKey($key)) { throw "Unmapped vtable $($p.Value[$i])" }
                                $p.Value[$i] = $map[$key]
                            }
                        } elseif ($p.Value -is [string]) {
                            $key = [Convert]::ToUInt32($p.Value.Substring(2), 16)
                            if (!$map.ContainsKey($key)) { throw "Unmapped vtable $($p.Value)" }
                            $p.Value = $map[$key]
                        }
                    } else { ReplaceVtables $p.Value }
                }
            } elseif ($value -is [array]) { foreach ($child in $value) { ReplaceVtables $child } }
        }
        ReplaceVtables $profile
        function PickRoot([string]$kind, [int]$min, [int]$max, [string]$slot = '', [string]$offset = '') {
            $found = @($lines | Where-Object { $_[0] -eq 'ROOT' -and $_[1] -eq $kind -and [int]$_[4] -ge $min -and [int]$_[4] -le $max -and (!$slot -or $_[2] -eq $slot) -and (!$offset -or $_[3] -eq $offset) })
            if ($found.Count -ne 1) { throw "Root $kind not unique ($($found.Count)); profile not written" }
            return ,$found[0]
        }
        function PickDirect([string]$kind) {
            $found = @($lines | Where-Object { $_[0] -eq 'DIRECT' -and $_[1] -eq $kind })
            if ($found.Count -eq 0) { throw "Direct vector $kind not found; profile not written" }
            # The same fingerprint exposes several equally sized player vectors and distinct team
            # vectors; these two roots were identified by FMLibreEditor's player profile and by
            # live team-vtable histogram (43,231 club / 1,770 national teams), respectively.
            $known = switch ($kind) { 'players' { '0x5683DC0' } 'teams' { '0x5684050' } default { '' } }
            if ($known) { $found = @($found | Where-Object { $_[2] -eq $known }) }
            if ($found.Count -eq 0) { throw "Verified direct vector $kind not present; profile not written" }
            if ($found.Count -gt 1) {
                $pairs = @($found | ForEach-Object { $_[4] + ':' + $_[5] } | Select-Object -Unique)
                if ($pairs.Count -ne 1) { throw "Direct vector $kind has distinct candidates; profile not written" }
                $found = @($found | Sort-Object { [Convert]::ToUInt32($_[2].Substring(2), 16) } | Select-Object -First 1)
            }
            return ,$found[0]
        }
        $players = PickDirect 'players'; $teams = PickDirect 'teams'; $staff = PickDirect 'staff'
        $comp = PickRoot 'competition.vtable' 1000 100000 '' '0x40'
        $nation = PickRoot 'nation.vtable' 100 1000 $comp[2]
        $stadium = PickRoot 'stadium.vtable' 1000 200000 $comp[2]
        $award = PickRoot 'award.vtable' 1 999 '' '0x30'
        $cities = PickRoot 'player.cities.vtable' 1000 200000 '' '0x0'
        $proposals = PickRoot 'player.proposals.transferVtable' 1000 100000 '' '0x8'
        $buildLine = @($lines | Where-Object { $_[0] -eq 'BUILD' } | Select-Object -First 1)
        $fingerprint = @($lines | Where-Object { $_[0] -eq 'FINGERPRINT' })
        if ($buildLine.Count -ne 1 -or $fingerprint.Count -ne 1 -or $fingerprint[0][1] -ne '0x175DA000' -or $fingerprint[0][2] -ne '0x6AE563A1') { throw 'Unsupported Game Pass fingerprint; profile not written' }
        $profile | Add-Member -NotePropertyName matchBuild -NotePropertyValue $profile.build
        $profile.build += ' Game Pass'
        $profile.buildStringRva = $buildLine[0][1]
        $profile | Add-Member -NotePropertyName imageSize -NotePropertyValue $fingerprint[0][1]
        $profile | Add-Member -NotePropertyName peTimestamp -NotePropertyValue $fingerprint[0][2]
        $profile.playerList.rootRva = $players[2]; $profile.playerList.beginOffset = '0x0'; $profile.playerList.endOffset = '0x8'
        $profile.player.cities.listRootRva = $cities[2]
        $profile.player.proposals.listRootRva = $proposals[2]; $profile.player.proposals.listOffset = $proposals[3]
        $profile.player.callUps.listRva = $teams[2]
        $profile.nation.listRootRva = $nation[2]; $profile.nation.listOffset = $nation[3]
        $profile.stadium.listRootRva = $stadium[2]; $profile.stadium.listOffset = $stadium[3]
        $profile.competition.listRootRva = $comp[2]; $profile.competition.listOffset = $comp[3]
        $profile.award.listRootRva = $award[2]; $profile.award.listOffset = $award[3]
        $profile.staff.personListRootRva = $staff[2]; $profile.staff.personListOffset = '0x0'
        $profile.staff | Add-Member -NotePropertyName directStaffVector -NotePropertyValue $true
        $kinds = @($lines | Where-Object { $_[0] -eq 'STAFFKIND' })
        $hybrid = @($kinds | Where-Object { $_[1] -ne $profile.staff.kinds[0].staffVtable -and $_[1] -ne $profile.staff.kinds[1].staffVtable -and $_[3] -eq '0x100' })
        if ($hybrid.Count -ne 1) { throw "Hybrid staff kind not unique ($($hybrid.Count)); profile not written" }
        $profile.staff.kinds = @($profile.staff.kinds) + @([pscustomobject]@{ staffVtable = $hybrid[0][1]; personVtable = $hybrid[0][2]; staffPart = $hybrid[0][3] })
        $profile.PSObject.Properties.Remove('gameDate')
        $profile | Add-Member -NotePropertyName directGameDateRva -NotePropertyValue '0x56257C8'
        $json = $profile | ConvertTo-Json -Depth 60
        [System.IO.File]::WriteAllText($ProfileOutput, $json, [System.Text.UTF8Encoding]::new($false))
        "PROFILE`t$ProfileOutput"
    }
} finally { $stream.Dispose() }
