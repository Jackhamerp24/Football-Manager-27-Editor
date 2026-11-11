using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Competitions;

/// <summary>Snapshot of the copies FMRTE edits for a squad rule: three for a competition with a nation object,
/// one for a competition without it (UEFA Champions League, measured 5/11/2026).</summary>
public sealed class CompetitionSquadRules
{
    public CompetitionRecord Competition { get; }
    public int MaximumSquadSize { get; }
    /// <summary>True when the game stores the rule as a minimum (flag 1), false for a maximum (flag 0xFF), null when the flag is another value.</summary>
    public bool? IsMinimum { get; }
    /// <summary>Competition ids whose rule record holds the same value object (FMRTE's "Also Applies To").</summary>
    public IReadOnlyList<int> AlsoAppliesTo { get; }
    internal ulong[] Addresses { get; }
    internal int[] Sizes { get; }
    internal IReadOnlyDictionary<(ulong Address, int Length), byte[]> Reads { get; }
    internal CompetitionSquadRules(CompetitionRecord competition, int maximum, ulong[] addresses,
        IReadOnlyDictionary<(ulong, int), byte[]> reads, int[]? sizes = null, IReadOnlyList<int>? alsoAppliesTo = null,
        bool? isMinimum = null)
        => (Competition, MaximumSquadSize, Addresses, Reads, Sizes, AlsoAppliesTo, IsMinimum) =
            (competition, maximum, addresses, reads, sizes ?? Enumerable.Repeat(4, addresses.Length).ToArray(),
             alsoAppliesTo ?? [], isMinimum);
}

/// <summary>One rule record in the competition's rule group, including conditional variants that share a code.</summary>
public sealed record CompetitionRuleVariant(int Code, int Value, bool? IsMinimum, string Condition);

/// <summary>
/// Squadron registration rules follow two representations: an inline rule vector and two
/// nested dictionaries. FMRTE writes all three; incomplete or ambiguous structures are unavailable.
/// </summary>
public sealed class CompetitionSquadRulesAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    sealed class Reader(IProcessMemory memory)
    {
        public Dictionary<(ulong, int), byte[]> Reads { get; } = [];
        public byte[] Bytes(ulong address, int length)
        {
            if (address < 0x10000 || address > 0x7FFFFFFFFFFFUL || length < 0 || length > 65536 || address + (ulong)length > 0x800000000000UL)
                throw new InvalidOperationException("Invalid Rules address");
            var result = memory.Read(address, length);
            Reads[(address, length)] = result;
            return result;
        }
        public ulong Q(ulong address) => BitConverter.ToUInt64(Bytes(address, 8));
        public int I(ulong address) => BitConverter.ToInt32(Bytes(address, 4));
        public ushort U16(ulong address) => BitConverter.ToUInt16(Bytes(address, 2));
        public byte B(ulong address) => Bytes(address, 1)[0];
        public string Key(ulong address) => Convert.ToHexString(Bytes(address, 4));
        /// <summary>Rule records keep a direction flag at +0x1E: 1 means a minimum, 0xFF a maximum.</summary>
        public bool? Minimum(ulong rule) => U16(rule + 0x1E) switch { 1 => true, 0x00FF => false, _ => null };
        public ulong[] Vector(ulong header, int stride = 8)
        {
            var h = Bytes(header, 16);
            ulong begin = BitConverter.ToUInt64(h), end = BitConverter.ToUInt64(h, 8);
            if (begin == 0 && end == 0) return [];
            if (begin == 0 || end < begin || (end - begin) % (ulong)stride != 0 || (end - begin) / (ulong)stride > 4096)
                throw new InvalidOperationException("Invalid Rules vector");
            return Enumerable.Range(0, checked((int)((end - begin) / (ulong)stride))).Select(i => begin + (ulong)(i * stride)).ToArray();
        }
        public ulong[] Pointers(ulong header) => Vector(header).Select(Q).ToArray();
        public Dictionary<string, ulong> Dictionary(ulong header)
        {
            var result = new Dictionary<string, ulong>();
            foreach (ulong p in Vector(header, 16))
            {
                string key = Encoding.ASCII.GetString(Bytes(p, 4));
                if (!result.TryAdd(key, Q(p + 8))) throw new InvalidOperationException("Duplicate Rules key");
            }
            return result;
        }
        public Dictionary<string, ulong> Node(ulong node) => Dictionary(Q(node + 8));
    }

    public CompetitionSquadRules? Read(CompetitionRecord competition, IReadOnlyList<CompetitionRecord>? peers = null) => ReadRule(competition, 9, null, peers) ?? ReadFallbackMaximum(competition);
    /// <summary>
    /// Every rule code present in the competition's rule group, without assuming a fixed list of leagues.
    /// Returns an empty list when the holder/group cannot be resolved.
    /// </summary>
    public IReadOnlyList<int> RuleCodes(CompetitionRecord competition)
    {
        if (profile.Competition?.PrizeMoney is not { } prize || profile.Competition.Id is not { } idOffset ||
            profile.Competition.SquadRulesContainerVtableRva is null) return [];
        try
        {
            var r = new Reader(memory);
            if (r.Q(competition.Address) != moduleBase + profile.Competition.VtableRva ||
                r.I(competition.Address + (ulong)idOffset) != competition.Id) return [];
            ulong holder = r.Q(competition.Address + 0xB0);
            if (holder == 0 || r.Q(holder) != moduleBase + prize.HolderVtableRva) return [];
            byte index = r.B(holder + 0x31);
            ulong common = r.Q(holder + 0x370), groups = common == 0 ? 0 : r.Q(common + 0x1E0);
            if (groups == 0) return [];
            var group = r.Pointers(groups + 0x60).Where(p => r.B(p + 0x120) == index).ToArray();
            if (group.Length != 1) return [];
            return r.Pointers(r.Q(group[0] + 0x70)).Select(p => (int)r.U16(p + 0x1C)).Where(code => code > 0).Distinct().OrderBy(code => code).ToArray();
        }
        catch (Exception ex) when (ex is MemoryAccessException or InvalidOperationException or OverflowException or ArgumentException) { return []; }
    }

    /// <summary>Reads the primary value for a code even when the multi-copy snapshot cannot be validated for writing.</summary>
    public int? ReadPrimaryValue(CompetitionRecord competition, int ruleCode)
    {
        if (profile.Competition?.PrizeMoney is not { } prize || profile.Competition.Id is not { } idOffset ||
            profile.Competition.SquadRulesContainerVtableRva is null) return null;
        try
        {
            var r = new Reader(memory);
            if (r.Q(competition.Address) != moduleBase + profile.Competition.VtableRva ||
                r.I(competition.Address + (ulong)idOffset) != competition.Id) return null;
            ulong holder = r.Q(competition.Address + 0xB0);
            if (holder == 0 || r.Q(holder) != moduleBase + prize.HolderVtableRva) return null;
            byte index = r.B(holder + 0x31);
            ulong common = r.Q(holder + 0x370), groups = common == 0 ? 0 : r.Q(common + 0x1E0);
            if (groups == 0) return null;
            var group = r.Pointers(groups + 0x60).Where(p => r.B(p + 0x120) == index).ToArray();
            if (group.Length != 1) return null;
            var rule = r.Pointers(r.Q(group[0] + 0x70)).FirstOrDefault(p => r.U16(p + 0x1C) == ruleCode);
            return rule == 0 ? null : r.I(rule + 0x18);
        }
        catch (Exception ex) when (ex is MemoryAccessException or InvalidOperationException or OverflowException or ArgumentException) { return null; }
    }


    /// <summary>Every rule record in the group, including same-code conditional variants. Empty when the group is unavailable.</summary>
    public IReadOnlyList<CompetitionRuleVariant> ReadVariants(CompetitionRecord competition)
    {
        if (profile.Competition?.PrizeMoney is not { } prize || profile.Competition.Id is not { } idOffset ||
            profile.Competition.SquadRulesContainerVtableRva is null) return [];
        try
        {
            var r = new Reader(memory);
            if (r.Q(competition.Address) != moduleBase + profile.Competition.VtableRva ||
                r.I(competition.Address + (ulong)idOffset) != competition.Id) return [];
            ulong holder = r.Q(competition.Address + 0xB0);
            if (holder == 0 || r.Q(holder) != moduleBase + prize.HolderVtableRva) return [];
            byte index = r.B(holder + 0x31);
            ulong common = r.Q(holder + 0x370), groups = common == 0 ? 0 : r.Q(common + 0x1E0);
            if (groups == 0) return [];
            var group = r.Pointers(groups + 0x60).Where(p => r.B(p + 0x120) == index).ToArray();
            if (group.Length != 1) return [];
            return r.Pointers(r.Q(group[0] + 0x70)).Select(p => new CompetitionRuleVariant(
                (int)r.U16(p + 0x1C), r.I(p + 0x18), r.Minimum(p),
                r.Key(p) == "00000000" ? "" : r.Key(p))).ToArray();
        }
        catch (Exception ex) when (ex is MemoryAccessException or InvalidOperationException or OverflowException or ArgumentException) { return []; }
    }

    CompetitionSquadRules? ReadFallbackMaximum(CompetitionRecord competition)
    {
        if (profile.Competition?.PrizeMoney is not { } prize || profile.Competition.Id is not { } idOffset ||
            profile.Competition.SquadRulesContainerVtableRva is not { } containerVtable || profile.Nation is not { } nation) return null;
        try
        {
            var r = new Reader(memory);
            if (r.Q(competition.Address) != moduleBase + profile.Competition.VtableRva || r.I(competition.Address + (ulong)idOffset) != competition.Id) return null;
            ulong holder = r.Q(competition.Address + 0xB0);
            if (r.Q(holder) != moduleBase + prize.HolderVtableRva) return null;
            byte index = r.B(holder + 0x31);
            ulong groups = r.Q(r.Q(holder + 0x370) + 0x1E0);
            var group = r.Pointers(groups + 0x60).Where(p => r.B(p + 0x120) == index).ToArray();
            if (group.Length != 1) return null;
            // Do not use fallback if an explicit maximum rule exists but failed validation.
            if (r.Pointers(r.Q(group[0] + 0x70)).Any(p => r.U16(p + 0x1C) == 9)) return null;
            ulong at = group[0] + 0xF0; int amount = r.B(at);
            ulong detail = r.Q(competition.Address + 0x60);
            if (r.Q(detail) != moduleBase + nation.VtableRva) return null;
            ulong root = r.Q(detail + 0x108);
            if (r.Q(root) != moduleBase + containerVtable) return null;
            var sources = new List<Dictionary<string, ulong>> { r.Dictionary(root + 0x570) };
            sources.AddRange(r.Pointers(root + 0x588).Select(r.Node));
            var copies = new List<ulong>();
            foreach (var source in sources)
            {
                if (!source.TryGetValue("lrxf", out ulong list)) continue;
                var items = r.Pointers(r.Q(list + 8));
                if (index >= items.Length) continue;
                var record = r.Node(items[index]);
                if (record.TryGetValue("pmoc", out ulong compNode))
                {
                    var identity = r.Node(compNode);
                    if (!identity.TryGetValue("pmoc", out ulong valueNode) || r.I(valueNode + 8) != competition.Id) continue;
                }
                if (!record.TryGetValue("prns", out ulong rulesNode)) continue;
                var nested = r.Node(rulesNode);
                if (!nested.TryGetValue("dnss", out ulong settingsNode)) continue;
                var settings = r.Node(settingsNode);
                if (settings.TryGetValue("smns", out ulong value)) copies.Add(value + 8);
            }
            var addresses = new[] { at }.Concat(copies).ToArray();
            if (addresses.Length != 3 || addresses.Distinct().Count() != 3 || copies.Any(p => r.I(p) != amount)) return null;
            return new(competition, amount, addresses, r.Reads, [1, 4, 4], isMinimum: false);
        }
        catch (Exception ex) when (ex is MemoryAccessException or InvalidOperationException or OverflowException or ArgumentException) { return null; }
    }

    public CompetitionSquadRules? ReadHomegrown(CompetitionRecord competition, IReadOnlyList<CompetitionRecord>? peers = null) => competition.Id switch
    {
        11 => ReadRule(competition, 0x92, null, peers),
        12 => ReadRule(competition, 0xFD, null, peers),
        22 => ReadRule(competition, 0x27, null, peers),
        23 => ReadRule(competition, 0x27, null, peers),
        _ => null,
    };

    /// <summary>FMRTE's "Also Applies To": competitions whose rule entry is the same value object. A competition
    /// resolves through its holder to a shared common object and one group index, so equal (common, index) means the
    /// same group, the same rule record and therefore the same value address. Verified on the three UEFA competitions,
    /// which share Maximum squad size 26 although each has its own holder (5/11/2026).</summary>
    HashSet<int> SharedGroup(IReadOnlyList<CompetitionRecord>? peers, CompetitionRecord self, ulong common, byte index)
    {
        var result = new HashSet<int>();
        if (peers is null || common == 0 || profile.Competition?.PrizeMoney is not { } prize) return result;
        foreach (var peer in peers)
        {
            if (peer.Address == self.Address) continue;
            try
            {
                ulong holder = memory.ReadUInt64(peer.Address + 0xB0);
                if (holder == 0 || memory.ReadUInt64(holder) != moduleBase + prize.HolderVtableRva) continue;
                if (memory.ReadUInt64(holder + 0x370) != common) continue;
                if (memory.Read(holder + 0x31, 1)[0] == index) result.Add(peer.Id);
            }
            catch (MemoryAccessException) { }
        }
        return result;
    }

    public CompetitionSquadRules? ReadHomegrownClub(CompetitionRecord competition, IReadOnlyList<CompetitionRecord>? peers = null) => competition.Id switch
    {
        22 or 23 => ReadRule(competition, 0x26, null, peers),
        _ => null,
    };

    /// <param name="peers">Optional full competition list. When given, competitions that resolve to the same
    /// rules common object are reported in <see cref="CompetitionSquadRules.AlsoAppliesTo"/>; FMRTE shows the
    /// three UEFA competitions through this shared object even though each has its own holder.</param>
    public CompetitionSquadRules? ReadRule(CompetitionRecord competition, int ruleCode, Action<string>? trace = null,
        IReadOnlyList<CompetitionRecord>? peers = null)
    {
        CompetitionSquadRules? Fail(string reason) { trace?.Invoke(reason); return null; }
        if (profile.Competition?.PrizeMoney is not { } prize || profile.Competition.Id is not { } idOffset ||
            profile.Competition.SquadRulesContainerVtableRva is not { } containerVtable || profile.Nation is not { } nation) return Fail("profile lacks rules offsets");
        try
        {
            var r = new Reader(memory);
            if (r.Q(competition.Address) != moduleBase + profile.Competition.VtableRva ||
                r.I(competition.Address + (ulong)idOffset) != competition.Id) return Fail("competition identity changed");
            ulong holder = r.Q(competition.Address + 0xB0);
            trace?.Invoke($"holder 0x{holder:X} vtable 0x{(holder == 0 ? 0 : r.Q(holder)):X} (want 0x{moduleBase + prize.HolderVtableRva:X})");
            if (holder == 0 || r.Q(holder) != moduleBase + prize.HolderVtableRva) return Fail("holder vtable mismatch");
            byte index = r.B(holder + 0x31);
            ulong common = r.Q(holder + 0x370), groups = common == 0 ? 0 : r.Q(common + 0x1E0);
            trace?.Invoke($"index {index}, common 0x{common:X}, groups 0x{groups:X}");
            if (groups == 0) return Fail("groups vector missing");
            var groupPointers = r.Pointers(groups + 0x60);
            trace?.Invoke($"groups +0x60: {groupPointers.Length} entries; indexes {string.Join(",", groupPointers.Select(p => r.B(p + 0x120)))}");
            var group = groupPointers.Where(p => r.B(p + 0x120) == index).ToArray();
            if (group.Length != 1) return Fail($"group match count {group.Length}");
            var rulePointers = r.Pointers(r.Q(group[0] + 0x70));
            trace?.Invoke($"rules in group: {rulePointers.Length}; codes {string.Join(",", rulePointers.Select(p => $"0x{r.U16(p + 0x1C):X}"))}");
            var primary = rulePointers.Where(p => r.U16(p + 0x1C) == ruleCode).ToArray();
            if (primary.Length != 1) return Fail($"rule code 0x{ruleCode:X} matches {primary.Length}");
            ulong primaryValue = primary[0] + 0x18;
            int amount = r.I(primaryValue);
            bool? isMinimum = r.Minimum(primary[0]);
            trace?.Invoke($"primary value 0x{primaryValue:X} = {amount}");
            var sharedCommons = SharedGroup(peers, competition, common, index);
            trace?.Invoke($"peer competitions sharing this group: {sharedCommons.Count}");
            ulong detail = r.Q(competition.Address + 0x60);
            if (detail == 0)
            {
                // Competition has no nation/rules object (e.g. UEFA Champions League): FMRTE wrote exactly one
                // copy of Maximum squad size here (0x2722FCEB928, 25 -> 26, measured 5/11/2026 with a Frida hook).
                trace?.Invoke("no nation/rules object: single-copy rule");
                return new(competition, amount, [primaryValue], r.Reads, [4], sharedCommons.OrderBy(id => id).ToList(), isMinimum);
            }
            if (r.Q(detail) != moduleBase + nation.VtableRva) return Fail($"detail 0x{detail:X} is not the nation/rules object");
            ulong root = r.Q(detail + 0x108);
            if (root == 0 || r.Q(root) != moduleBase + containerVtable) return Fail($"rules root 0x{root:X} vtable mismatch");
            trace?.Invoke($"rules root 0x{root:X}");

            var sources = new List<Dictionary<string, ulong>> { r.Dictionary(root + 0x570) };
            var extra = r.Pointers(root + 0x588);
            trace?.Invoke($"sources: base dictionary + {extra.Length} nodes");
            sources.AddRange(extra.Select(r.Node));
            var matches = new List<ulong>();
            var sharing = new SortedSet<int>();
            for (int s = 0; s < sources.Count; s++)
            {
                var source = sources[s];
                if (!source.TryGetValue("lrxf", out ulong list)) { trace?.Invoke($"source {s}: no lrxf"); continue; }
                var items = r.Pointers(r.Q(list + 8));
                trace?.Invoke($"source {s}: {items.Length} competition entries, index {index}");
                if (index >= items.Length) continue;
                var record = r.Node(items[index]);
                if (record.TryGetValue("pmoc", out ulong compNode))
                {
                    var identity = r.Node(compNode);
                    bool ok = identity.TryGetValue("pmoc", out ulong valueNode) && r.I(valueNode + 8) == competition.Id;
                    trace?.Invoke($"source {s}: identity check {(ok ? "match" : "MISMATCH")}");
                    if (!ok) continue;
                }
                else trace?.Invoke($"source {s}: no pmoc identity");
                if (!record.TryGetValue("rsqs", out ulong rulesNode)) { trace?.Invoke($"source {s}: no rsqs"); continue; }
                int count = 0;
                foreach (ulong rule in r.Pointers(r.Q(rulesNode + 8)))
                {
                    var fields = r.Node(rule);
                    if (!fields.TryGetValue("epyt", out ulong type) || !fields.TryGetValue("ulav", out ulong value)) continue;
                    if (r.I(type + 8) == ruleCode) { matches.Add(value + 8); count++; }
                }
                trace?.Invoke($"source {s}: {count} copies of code 0x{ruleCode:X}");

                // FMRTE's "Also Applies To": other entries of the same list whose value object is one we are about to write.
                var ours = new HashSet<ulong>(matches) { primaryValue };
                for (int j = 0; j < items.Length; j++)
                {
                    if (j == index) continue;
                    var other = r.Node(items[j]);
                    int? otherId = null;
                    if (other.TryGetValue("pmoc", out ulong otherComp) &&
                        r.Node(otherComp).TryGetValue("pmoc", out ulong otherValue)) otherId = r.I(otherValue + 8);
                    if (!other.TryGetValue("rsqs", out ulong otherRules)) continue;
                    foreach (ulong rule in r.Pointers(r.Q(otherRules + 8)))
                    {
                        var fields = r.Node(rule);
                        if (!fields.TryGetValue("epyt", out ulong type) || !fields.TryGetValue("ulav", out ulong value)) continue;
                        if (r.I(type + 8) != ruleCode) continue;
                        if (!ours.Contains(value + 8)) continue;
                        if (otherId is { } id && id != competition.Id) sharing.Add(id);
                    }
                }
            }
            var addresses = new[] { primaryValue }.Concat(matches).ToArray();
            trace?.Invoke($"also applies to {sharing.Count} competitions");
            trace?.Invoke($"copies total {addresses.Length}, values {string.Join(",", addresses.Select(a => r.I(a)))}");
            // The rules containers hold one copy for competitions the game builds itself (MLS: four shared
            // competitions, no rsqs records). FMRTE writes that copy alone, so accept a single validated value.
            if (addresses.Length == 1)
                return new(competition, amount, [primaryValue], r.Reads, [4], sharedCommons.OrderBy(id => id).ToList(), isMinimum);
            if (addresses.Length != 3 || addresses.Distinct().Count() != 3 || addresses.Any(a => r.I(a) != amount)) return Fail("copy set is not exactly three equal values");
            return new(competition, amount, addresses, r.Reads, alsoAppliesTo: sharing.Union(sharedCommons).OrderBy(id => id).ToList(), isMinimum: isMinimum);
        }
        catch (Exception ex) when (ex is MemoryAccessException or InvalidOperationException or OverflowException or ArgumentException)
        { return Fail($"exception: {ex.Message}"); }
    }

    public void SetMaximumSquadSize(CompetitionSquadRules snapshot, int value) => SetMany([(snapshot, value)]);

    public void SetMany(IReadOnlyList<(CompetitionSquadRules Snapshot, int Value)> edits)
    {
        if (edits.Any(edit => edit.Value < 0)) throw new ArgumentOutOfRangeException(nameof(edits));
        if (edits.Any(edit => edit.Snapshot.Sizes.Contains(1) && edit.Value > byte.MaxValue)) throw new ArgumentOutOfRangeException(nameof(edits));
        if (edits.SelectMany(edit => edit.Snapshot.Addresses).GroupBy(address => address).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Squad rule edits overlap");
        foreach (var edit in edits)
        foreach (var (key, expected) in edit.Snapshot.Reads)
        {
            var current = new byte[key.Length];
            if (!memory.TryRead(key.Address, current) || !current.AsSpan().SequenceEqual(expected))
                throw new StaleObjectException("Rules changed since they were read");
        }
        foreach (var edit in edits)
        for (int i = 0; i < edit.Snapshot.Addresses.Length; i++)
        {
            ulong address = edit.Snapshot.Addresses[i];
            byte[] after = edit.Snapshot.Sizes[i] == 1 ? [(byte)edit.Value] : BitConverter.GetBytes(edit.Value);
            if (!memory.TryWrite(address, after) || !memory.Read(address, after.Length).AsSpan().SequenceEqual(after))
                throw new MemoryAccessException("Could not write all squad rule copies");
        }
    }
}
