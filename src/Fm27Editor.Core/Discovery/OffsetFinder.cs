using System.Text;
using System.Text.RegularExpressions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Verification;

namespace Fm27Editor.Core.Discovery;

/// <summary>
/// Rebuilds an <see cref="OffsetProfile"/> for an unknown game build from a running game with a
/// save loaded. Structure is found statistically over the heap; fields that statistics cannot
/// tell apart (names, height, date of birth) are located with the golden players.
/// </summary>
public sealed partial class OffsetFinder(WindowsProcessMemory memory, GameProcess game, Action<string> log)
{
    const int ScanLimit = 0x400;
    const int PositionCount = 15;   // includes one unnamed slot (FM26 offset 0x99)
    const int AttributeCount = 54;

    /// <summary>Attribute order inside the block, as FMRTE 26 lays it out (FM26 offsets 0xA7..0xDC).</summary>
    static readonly PlayerAttribute[] AttributeOrder =
    [
        PlayerAttribute.Crossing, PlayerAttribute.Dribbling, PlayerAttribute.Finishing, PlayerAttribute.Heading,
        PlayerAttribute.LongShots, PlayerAttribute.Marking, PlayerAttribute.OffTheBall, PlayerAttribute.Passing,
        PlayerAttribute.PenaltyTaking, PlayerAttribute.Tackling, PlayerAttribute.Vision, PlayerAttribute.Handling,
        PlayerAttribute.AerialReach, PlayerAttribute.CommandOfArea, PlayerAttribute.Communication, PlayerAttribute.Kicking,
        PlayerAttribute.Throwing, PlayerAttribute.Anticipation, PlayerAttribute.Decisions, PlayerAttribute.OneOnOnes,
        PlayerAttribute.Positioning, PlayerAttribute.Reflexes, PlayerAttribute.FirstTouch, PlayerAttribute.Technique,
        PlayerAttribute.LeftFoot, PlayerAttribute.RightFoot, PlayerAttribute.Flair, PlayerAttribute.Corners,
        PlayerAttribute.Teamwork, PlayerAttribute.WorkRate, PlayerAttribute.LongThrows, PlayerAttribute.Eccentricity,
        PlayerAttribute.RushingOut, PlayerAttribute.TendencyToPunch, PlayerAttribute.Acceleration, PlayerAttribute.FreeKicks,
        PlayerAttribute.Strength, PlayerAttribute.Stamina, PlayerAttribute.Pace, PlayerAttribute.JumpingReach,
        PlayerAttribute.Leadership, PlayerAttribute.Dirtiness, PlayerAttribute.Balance, PlayerAttribute.Bravery,
        PlayerAttribute.Consistency, PlayerAttribute.Aggression, PlayerAttribute.Agility, PlayerAttribute.ImportantMatches,
        PlayerAttribute.InjuryProneness, PlayerAttribute.Versatility, PlayerAttribute.NaturalFitness, PlayerAttribute.Determination,
        PlayerAttribute.Composure, PlayerAttribute.Concentration,
    ];

    /// <summary>Position slot index inside the 15-byte block (FM26 0x98..0xA6); slot 1 is unnamed.</summary>
    static readonly (PlayerPosition Position, int Slot)[] PositionSlots =
    [
        (PlayerPosition.Goalkeeper, 0), (PlayerPosition.DefenderLeft, 2), (PlayerPosition.DefenderCentral, 3),
        (PlayerPosition.DefenderRight, 4), (PlayerPosition.DefensiveMidfielder, 5), (PlayerPosition.MidfielderLeft, 6),
        (PlayerPosition.MidfielderCentral, 7), (PlayerPosition.MidfielderRight, 8), (PlayerPosition.AttackingMidLeft, 9),
        (PlayerPosition.AttackingMidCentral, 10), (PlayerPosition.AttackingMidRight, 11), (PlayerPosition.Striker, 12),
        (PlayerPosition.WingBackLeft, 13), (PlayerPosition.WingBackRight, 14),
    ];

    public OffsetProfile Find(IReadOnlyList<GoldenPlayer> golden, DateOnly? gameDate = null)
    {
        var (build, buildRva) = FindBuildString();
        log($"build {build} at +0x{buildRva:X}");

        var heap = memory.EnumerateWritableRegions(game.ModuleBase, game.ModuleEnd).ToList();
        var (vtables, blockOffset, objects) = FindPlayerType(heap);
        log($"player vtables {string.Join(", ", vtables.Select(v => $"+0x{v:X}"))}, positions at +0x{blockOffset:X}, {objects.Count} objects");

        int objectSize = EstimateObjectSize(objects);
        log($"object size 0x{objectSize:X}");
        var (rootRva, count) = FindPlayerList(heap, objects);
        log($"player list root +0x{rootRva:X} ({count} entries)");

        // Heap scans also hit stale copies of player objects (their number grows as days pass), so
        // statistics only use objects the game's own player list points at.
        var listed = ReadList(rootRva).ToHashSet();
        objects = objects.Where(listed.Contains).ToList();
        log($"{objects.Count} objects are in the player list");
        var samples = objects.Select(o => memory.Read(o, objectSize)).ToList();
        var (ca, pa) = FindAbilityPair(samples, objectSize);
        log($"CA +0x{ca:X}, PA +0x{pa:X}");
        int personality = FindPersonality(samples, objectSize, blockOffset);
        log($"personality +0x{personality:X}");

        int attrStart = blockOffset + PositionCount;
        var attributes = AttributeOrder.Select((a, i) => (a, attrStart + i)).ToDictionary(x => x.a, x => x.Item2);
        var positions = PositionSlots.ToDictionary(p => p.Position, p => blockOffset + p.Slot);
        var personalityMap = Enum.GetValues<PersonalityAttribute>().ToDictionary(p => p, p => personality + (int)p);

        var located = LocateGoldenObjects(golden, objects, samples, attributes);
        log($"golden players located: {located.Count}/{golden.Count}");
        int height = FindInt16(located, g => g.Height) ?? throw new DiscoveryException("height field not found");
        int dob = FindDate(located) ?? throw new DiscoveryException("date of birth field not found");
        int first = FindName(located, g => g.FirstName, indirect: true) ?? throw new DiscoveryException("first name field not found");
        int last = FindName(located, g => g.LastName, indirect: true) ?? throw new DiscoveryException("last name field not found");
        int common = FindCommonName(located) ?? throw new DiscoveryException("common name field not found");
        int full = FindFullName(located, first, last, common);
        log($"height +0x{height:X}, birth date +0x{dob:X}, names first +0x{first:X} last +0x{last:X} common +0x{common:X} full +0x{full:X}");
        GameDateOffsets? dateChains = null;
        if (gameDate is { } today)
        {
            dateChains = FindGameDate(heap, today);
            log(dateChains is null ? "game date chain not found"
                : $"game date via +0x{dateChains.Primary.RootRva:X} -> +0x{dateChains.Primary.PointerOffset:X} -> +0x{dateChains.Primary.DateOffset:X}" +
                  (dateChains.CrossCheck is { } x ? $", cross-check +0x{x.RootRva:X} -> +0x{x.PointerOffset:X} -> +0x{x.DateOffset:X}" : ""));
        }
        var (contract, club) = FindContractAndClub(located);
        if (contract is null) log("contract/club not found (golden players need wage, contractExpires and club)");
        else log($"contract +0x{contract.PlayerContract:X} (wage +0x{contract.Wage:X}, expiry +0x{contract.ExpiryDate:X}, team +0x{contract.Team:X} -> club +0x{contract.TeamClub:X}), club name +0x{club!.Name:X}/+0x{club.ShortName:X}");

        return new OffsetProfile
        {
            Build = build,
            Module = GameProcess.DefaultModule,
            BuildStringRva = buildRva,
            PlayerListRootRva = rootRva,
            PlayerListBeginOffset = 0,
            PlayerListEndOffset = 8,
            PlayerVtableRvas = vtables,
            PlayerObjectSize = objectSize,
            AttributeScale = 5,
            Attributes = attributes,
            Positions = positions,
            Personality = personalityMap,
            Fields = new PlayerFieldOffsets
            {
                CurrentAbility = ca, PotentialAbility = pa,
                // Reputations sit right before CA in every build seen so far (FM26 and FM27).
                HomeReputation = ca - 6, CurrentReputation = ca - 4, WorldReputation = ca - 2,
                Height = height, DateOfBirth = dob,
                FullName = full, FirstName = first, LastName = last, CommonName = common,
            },
            Contract = contract,
            Club = club,
            GameDate = dateChains,
        };
    }

    /// <summary>
    /// Chains static qword → object → pointer → node whose bytes at +0..+0x100 hold today's date.
    /// Nodes are grouped by the node pointer itself, so one object holding the date twice counts once.
    /// Only nodes reached from at least two distinct static roots qualify. The primary is a node with
    /// a root whose object has a module vtable (then the most roots); the cross-check is the best other
    /// node. Within a node the chain with the smallest pointer offset is used.
    /// </summary>
    GameDateOffsets? FindGameDate(List<MemoryRegion> regions, DateOnly today)
    {
        const int ObjectSize = 0x400, DateWindow = 0x100;
        short day = (short)today.DayOfYear, year = (short)today.Year;
        var chains = new List<(ulong node, DateChain chain, bool vtable)>();
        var obj = new byte[ObjectSize];
        var window = new byte[DateWindow + 4];
        foreach (var r in regions.Where(r => r.IsModuleData))
        {
            var d = new byte[r.Size];
            if (!memory.TryRead(r.Start, d)) continue;
            for (int o = 0; o + 8 <= d.Length; o += 8)
            {
                ulong p = BitConverter.ToUInt64(d, o);
                if (p < 0x10000000000 || p > 0x7FF000000000 || (p >= game.ModuleBase && p < game.ModuleEnd)) continue;
                if (!memory.TryRead(p, obj)) continue;
                ulong vt = BitConverter.ToUInt64(obj);
                bool hasVtable = vt >= game.ModuleBase && vt < game.ModuleEnd;
                for (int k = 0; k + 8 <= ObjectSize; k += 8)
                {
                    ulong node = BitConverter.ToUInt64(obj, k);
                    if (node < 0x10000000000 || node > 0x7FF000000000 || !memory.TryRead(node, window)) continue;
                    for (int x = 0; x <= DateWindow; x += 2)
                        if (BitConverter.ToInt16(window, x) == day && BitConverter.ToInt16(window, x + 2) == year)
                        {
                            chains.Add((node, new DateChain((uint)(r.Start + (ulong)o - game.ModuleBase), k, x), hasVtable));
                            break;
                        }
                }
            }
        }
        var ranked = chains.GroupBy(c => c.node)
            .Select(g => (
                node: g.Key,
                roots: g.Select(c => c.chain.RootRva).Distinct().Count(),
                vtable: g.Any(c => c.vtable),
                best: g.OrderByDescending(c => c.vtable).ThenBy(c => c.chain.PointerOffset).ThenBy(c => c.chain.RootRva).First().chain))
            .Where(g => g.roots >= 2)
            .OrderByDescending(g => g.vtable).ThenByDescending(g => g.roots).ThenBy(g => g.best.PointerOffset)
            .ToList();
        if (ranked.Count == 0) return null;
        return new GameDateOffsets { Primary = ranked[0].best, CrossCheck = ranked.Count > 1 ? ranked[1].best : null };
    }

    /// <summary>
    /// Contract: a pointer in the player object to a node holding the golden wage (int32) and expiry
    /// (day, year). Club: from a pointer in that node, one more pointer, to an object whose string
    /// pointer equals the golden club name. Joined date and long name are assumed to sit next to
    /// expiry and short name, as in FM27 27.0.0.
    /// </summary>
    (ContractOffsets?, ClubOffsets?) FindContractAndClub(List<Located> located)
    {
        var withWage = located.Where(l => l.Golden.Wage is not null && l.Golden.ContractExpires is not null).ToList();
        var withClub = located.Where(l => l.Golden.Club is not null).ToList();
        if (withWage.Count == 0 || withClub.Count == 0) return (null, null);

        const int NodeSize = 0x60, TeamSize = 0x40, ClubSize = 0xD0;
        byte[]? Node(ulong p, int size) { if (p == 0) return null; var b = new byte[size]; return memory.TryRead(p, b) ? b : null; }

        for (int k = 0; k + 8 <= located[0].Data.Length; k += 8)
        {
            var nodes = withWage.Select(l => (l, n: Node(BitConverter.ToUInt64(l.Data, k), NodeSize))).ToList();
            if (nodes.Any(x => x.n is null)) continue;
            for (int w = 0; w + 4 <= NodeSize; w += 4)
            {
                if (!nodes.All(x => BitConverter.ToInt32(x.n!, w) == x.l.Golden.Wage)) continue;
                for (int e = 0; e + 4 <= NodeSize; e += 2)
                {
                    if (!nodes.All(x => BitConverter.ToInt16(x.n!, e) == x.l.Golden.ContractExpires!.Value.DayOfYear
                                        && BitConverter.ToInt16(x.n!, e + 2) == x.l.Golden.ContractExpires!.Value.Year)) continue;
                    ulong contractVt = BitConverter.ToUInt64(nodes[0].n!);
                    var clubNodes = withClub.Select(l => (l, n: Node(BitConverter.ToUInt64(l.Data, k), NodeSize))).ToList();
                    if (clubNodes.Any(x => x.n is null)) continue;
                    for (int t = 0; t + 8 <= NodeSize; t += 8)
                        for (int tc = 0; tc + 8 <= TeamSize; tc += 8)
                        {
                            var clubs = clubNodes.Select(x =>
                            {
                                var team = Node(BitConverter.ToUInt64(x.n!, t), TeamSize);
                                return (x.l, c: team is null ? null : Node(BitConverter.ToUInt64(team, tc), ClubSize));
                            }).ToList();
                            if (clubs.Any(x => x.c is null)) continue;
                            for (int s = 8; s + 8 <= ClubSize; s += 8)
                            {
                                if (!clubs.All(x => ReadName(BitConverter.ToUInt64(x.c!, s), indirect: false) == x.l.Golden.Club)) continue;
                                return (new ContractOffsets
                                {
                                    PlayerContract = k, ContractVtableRva = (uint)(contractVt - game.ModuleBase), Team = t,
                                    Wage = w, ExpiryDate = e, JoinedDate = e + 4, TeamClub = tc,
                                }, new ClubOffsets
                                {
                                    ClubVtableRva = (uint)(BitConverter.ToUInt64(clubs[0].c!) - game.ModuleBase), Name = s - 8, ShortName = s,
                                });
                            }
                        }
                }
            }
        }
        return (null, null);
    }

    [GeneratedRegex(@"2\d\.\d\.\d\+\d{7}")]
    private static partial Regex BuildPattern();

    (string, uint) FindBuildString()
    {
        const int Chunk = 1 << 20;
        var buf = new byte[Chunk + 32];
        for (ulong off = 0; off < (ulong)game.ModuleSize; off += Chunk)
        {
            int len = (int)Math.Min((ulong)buf.Length, (ulong)game.ModuleSize - off);
            if (!memory.TryRead(game.ModuleBase + off, buf.AsSpan(0, len))) continue;
            var m = BuildPattern().Match(Encoding.ASCII.GetString(buf, 0, len));
            if (m.Success) return (m.Value, (uint)(off + (ulong)m.Index));
        }
        throw new DiscoveryException("build string not found in game_plugin.dll");
    }

    /// <summary>
    /// Objects with a game_plugin vtable at +0 whose first run of 15 bytes in 1..20 followed by 54
    /// bytes in 1..100 starts at the same offset. The most common (vtable, offset) is the player;
    /// other vtables sharing that offset with many objects are player subclasses.
    /// </summary>
    (List<uint>, int, List<ulong>) FindPlayerType(List<MemoryRegion> regions)
    {
        var hist = new Dictionary<(uint vt, int off), List<ulong>>();
        foreach (var r in regions.Where(r => !r.IsModuleData && r.Size < 1UL << 31))
        {
            var d = new byte[r.Size];
            if (!memory.TryRead(r.Start, d)) continue;
            for (int o = 0; o + ScanLimit + PositionCount + AttributeCount <= d.Length; o += 8)
            {
                ulong vt = BitConverter.ToUInt64(d, o);
                if (vt < game.ModuleBase || vt >= game.ModuleEnd) continue;
                int k = FirstBlock(d, o);
                if (k < 0) continue;
                var key = ((uint)(vt - game.ModuleBase), k);
                if (!hist.TryGetValue(key, out var list)) hist[key] = list = new List<ulong>();
                list.Add(r.Start + (ulong)o);
            }
        }
        if (hist.Count == 0) throw new DiscoveryException("no player-like objects found; is a save loaded?");
        var best = hist.MaxBy(h => h.Value.Count);
        var subtypes = hist.Where(h => h.Key.off == best.Key.off && h.Value.Count >= 50).OrderByDescending(h => h.Value.Count).ToList();
        return (subtypes.Select(h => h.Key.vt).ToList(), best.Key.off, best.Value);
    }

    static int FirstBlock(byte[] d, int obj)
    {
        for (int k = 0x40; k < ScanLimit; k++)
        {
            int i = 0;
            while (i < PositionCount && d[obj + k + i] is >= 1 and <= 20) i++;
            if (i < PositionCount) continue;
            while (i < PositionCount + AttributeCount && d[obj + k + i] is >= 1 and <= 100) i++;
            if (i == PositionCount + AttributeCount) return k;
        }
        return -1;
    }

    /// <summary>Most common distance between consecutive objects in the same allocation run.</summary>
    static int EstimateObjectSize(List<ulong> objects)
    {
        var sorted = objects.OrderBy(o => o).ToArray();
        var gaps = new Dictionary<ulong, int>();
        for (int i = 1; i < sorted.Length; i++)
        {
            ulong g = sorted[i] - sorted[i - 1];
            if (g is > 0x100 and < 0x2000) gaps[g] = gaps.GetValueOrDefault(g) + 1;
        }
        return gaps.Count == 0 ? 0x400 : (int)gaps.MaxBy(g => g.Value).Key;
    }

    /// <summary>A pair of qwords in game_plugin static data bounding an array of player pointers.</summary>
    (uint, int) FindPlayerList(List<MemoryRegion> regions, List<ulong> objects)
    {
        var set = objects.ToHashSet();
        (uint rva, int count) best = (0, 0);
        foreach (var r in regions.Where(r => r.IsModuleData))
        {
            var d = new byte[r.Size];
            if (!memory.TryRead(r.Start, d)) continue;
            for (int o = 0; o + 16 <= d.Length; o += 8)
            {
                ulong begin = BitConverter.ToUInt64(d, o), end = BitConverter.ToUInt64(d, o + 8);
                if (begin == 0 || end <= begin || (end - begin) % 8 != 0) continue;
                ulong n = (end - begin) / 8;
                if (n < 1000 || n > 2_000_000 || (int)n <= best.count) continue;
                var sample = new byte[8 * 64];
                if (!memory.TryRead(begin, sample)) continue;
                int hits = Enumerable.Range(0, 64).Count(i => set.Contains(BitConverter.ToUInt64(sample, i * 8)));
                if (hits >= 48) best = ((uint)(r.Start + (ulong)o - game.ModuleBase), (int)n);
            }
        }
        return best.count > 0 ? best : throw new DiscoveryException("player list root not found");
    }

    ulong[] ReadList(uint rootRva)
    {
        ulong begin = memory.ReadUInt64(game.ModuleBase + rootRva), end = memory.ReadUInt64(game.ModuleBase + rootRva + 8);
        var raw = memory.Read(begin, (int)(end - begin));
        var list = new ulong[raw.Length / 8];
        Buffer.BlockCopy(raw, 0, list, 0, raw.Length);
        return list;
    }

    /// <summary>CA/PA: adjacent int16 pair, both in 1..200 and PA >= CA for (nearly) every player.</summary>
    static (int, int) FindAbilityPair(List<byte[]> samples, int size)
    {
        for (int k = 0; k + 4 <= size; k += 2)
        {
            int ok = samples.Count(s =>
            {
                short ca = BitConverter.ToInt16(s, k), pa = BitConverter.ToInt16(s, k + 2);
                return ca is >= 1 and <= 200 && pa is >= 1 and <= 200 && pa >= ca;
            });
            double avgCa = samples.Average(s => (double)BitConverter.ToInt16(s, k));
            if (ok >= samples.Count * 0.99 && avgCa > 20) return (k, k + 2);
        }
        throw new DiscoveryException("CA/PA pair not found");
    }

    /// <summary>Personality: 8 consecutive bytes in 1..20 for every player, outside the position block.</summary>
    static int FindPersonality(List<byte[]> samples, int size, int blockOffset)
    {
        for (int k = 0; k + 8 <= size; k++)
        {
            if (k + 8 > blockOffset && k < blockOffset + PositionCount) continue;
            int ok = samples.Count(s => s.AsSpan(k, 8).ToArray().All(b => b is >= 1 and <= 20));
            var distinct = samples.Take(2000).Select(s => s[k]).Distinct().Count();
            if (ok >= samples.Count * 0.99 && distinct > 10) return k;
        }
        throw new DiscoveryException("personality block not found");
    }

    sealed record Located(GoldenPlayer Golden, ulong Address, byte[] Data);

    List<Located> LocateGoldenObjects(IReadOnlyList<GoldenPlayer> golden, List<ulong> objects, List<byte[]> samples,
        Dictionary<PlayerAttribute, int> attributes)
    {
        var result = new List<Located>();
        foreach (var g in golden)
        {
            var hits = Enumerable.Range(0, objects.Count).Where(i =>
                g.Attributes.All(kv => AttributeScaling.ToDisplay(samples[i][attributes[kv.Key]], 5) == kv.Value)).ToList();
            if (hits.Count == 1) result.Add(new Located(g, objects[hits[0]], samples[hits[0]]));
            else log($"  {g.Name}: {hits.Count} matching objects, skipped");
        }
        if (result.Count < 3) throw new DiscoveryException("fewer than 3 golden players located; attribute layout may have changed");
        return result;
    }

    static int? FindInt16(List<Located> located, Func<GoldenPlayer, int?> value)
    {
        var withValue = located.Where(l => value(l.Golden) is not null).ToList();
        for (int k = 0; k + 2 <= located[0].Data.Length; k += 2)
            if (withValue.All(l => BitConverter.ToInt16(l.Data, k) == value(l.Golden))) return k;
        return null;
    }

    static int? FindDate(List<Located> located)
    {
        var withValue = located.Where(l => l.Golden.DateOfBirth is not null).ToList();
        for (int k = 0; k + 4 <= located[0].Data.Length; k += 2)
            if (withValue.All(l => BitConverter.ToInt16(l.Data, k) == l.Golden.DateOfBirth!.Value.DayOfYear
                                   && BitConverter.ToInt16(l.Data, k + 2) == l.Golden.DateOfBirth!.Value.Year))
                return k;
        return null;
    }

    int? FindName(List<Located> located, Func<GoldenPlayer, string?> name, bool indirect)
    {
        var withName = located.Where(l => name(l.Golden) is not null).ToList();
        if (withName.Count == 0) return null;
        for (int k = 0; k + 8 <= located[0].Data.Length; k += 8)
            if (withName.All(l => ReadName(BitConverter.ToUInt64(l.Data, k), indirect) == name(l.Golden))) return k;
        return null;
    }

    /// <summary>Holds the golden common names and is empty for players marked as having none
    /// (a first name can equal a common name, so the empty side is what tells them apart).</summary>
    int? FindCommonName(List<Located> located)
    {
        var named = located.Where(l => l.Golden.CommonName is not null).ToList();
        var unnamed = located.Where(l => l.Golden.NoCommonName).ToList();
        if (named.Count == 0 || unnamed.Count == 0) return null;
        for (int k = 0; k + 8 <= located[0].Data.Length; k += 8)
            if (named.All(l => ReadName(BitConverter.ToUInt64(l.Data, k), indirect: true) == l.Golden.CommonName)
                && unnamed.All(l => BitConverter.ToUInt64(l.Data, k) == 0))
                return k;
        return null;
    }

    /// <summary>The full name has no golden value; it is the one direct string pointer just before the name entries.</summary>
    int FindFullName(List<Located> located, int first, int last, int common)
    {
        int lowest = Math.Min(first, Math.Min(last, common));
        for (int k = lowest - 8; k >= lowest - 0x40; k -= 8)
            if (located.Any(l => ReadName(BitConverter.ToUInt64(l.Data, k), indirect: false) is { Length: > 3 } s
                                 && l.Golden.LastName is { } ln && s.Contains(ln)))
                return k;
        throw new DiscoveryException("full name field not found");
    }

    string? ReadName(ulong p, bool indirect)
    {
        if (p == 0) return null;
        if (indirect)
        {
            Span<byte> q = stackalloc byte[8];
            if (!memory.TryRead(p, q)) return null;
            p = BitConverter.ToUInt64(q);
        }
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n is <= 0 or > 256) return null;
        var bytes = new byte[n];
        return memory.TryRead(p + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}

public sealed class DiscoveryException(string message) : Exception(message);
