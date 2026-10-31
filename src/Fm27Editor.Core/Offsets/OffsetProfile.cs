using System.Globalization;
using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Offsets;

/// <summary>
/// Everything that changes between game builds: static RVAs inside game_plugin.dll and
/// field offsets inside game objects. One JSON file per build, see offsets/*.json.
/// </summary>
public sealed class OffsetProfile
{
    public required string Build { get; init; }
    public required string Module { get; init; }
    public required uint BuildStringRva { get; init; }

    public required uint PlayerListRootRva { get; init; }
    public required int PlayerListBeginOffset { get; init; }
    public required int PlayerListEndOffset { get; init; }
    public required IReadOnlyList<uint> PlayerVtableRvas { get; init; }

    public required int PlayerObjectSize { get; init; }
    public required int AttributeScale { get; init; }
    public required IReadOnlyDictionary<PlayerAttribute, int> Attributes { get; init; }
    public required IReadOnlyDictionary<PlayerPosition, int> Positions { get; init; }
    public required IReadOnlyDictionary<PersonalityAttribute, int> Personality { get; init; }
    public required PlayerFieldOffsets Fields { get; init; }
    public ContractOffsets? Contract { get; init; }
    public PendingContractOffsets? PendingContract { get; init; }
    public ClubOffsets? Club { get; init; }
    public TeamOffsets? Team { get; init; }
    public NationOffsets? Nation { get; init; }
    public CompetitionOffsets? Competition { get; init; }
    public GameDateOffsets? GameDate { get; init; }

    public static OffsetProfile Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = root.GetProperty("playerList");
        var player = root.GetProperty("player");
        return new OffsetProfile
        {
            Build = root.GetProperty("build").GetString() ?? throw new FormatException("build is missing"),
            Module = root.GetProperty("module").GetString() ?? throw new FormatException("module is missing"),
            BuildStringRva = (uint)Hex(root, "buildStringRva"),
            PlayerListRootRva = (uint)Hex(list, "rootRva"),
            PlayerListBeginOffset = Hex(list, "beginOffset"),
            PlayerListEndOffset = Hex(list, "endOffset"),
            PlayerVtableRvas = root.GetProperty("playerVtables").EnumerateArray().Select(v => (uint)ParseHex(v.GetString())).ToArray(),
            PlayerObjectSize = Hex(player, "objectSize"),
            AttributeScale = player.GetProperty("attributeScale").GetInt32(),
            Attributes = EnumMap<PlayerAttribute>(player.GetProperty("attributes")),
            Positions = EnumMap<PlayerPosition>(player.GetProperty("positions")),
            Personality = EnumMap<PersonalityAttribute>(player.GetProperty("personality")),
            Fields = new PlayerFieldOffsets
            {
                CurrentAbility = Hex(player, "currentAbility"),
                PotentialAbility = Hex(player, "potentialAbility"),
                HomeReputation = Hex(player, "homeReputation"),
                CurrentReputation = Hex(player, "currentReputation"),
                WorldReputation = Hex(player, "worldReputation"),
                Height = Hex(player, "height"),
                DateOfBirth = Hex(player, "dateOfBirth"),
                FullName = Hex(player, "fullName"),
                FirstName = Hex(player, "firstName"),
                LastName = Hex(player, "lastName"),
                CommonName = Hex(player, "commonName"),
                Id = OptHex(player, "id"),
            },
            Contract = root.TryGetProperty("contract", out var c) ? new ContractOffsets
            {
                PlayerContract = Hex(c, "playerContract"),
                ContractVtableRva = (uint)Hex(c, "vtable"),
                Team = Hex(c, "team"),
                Wage = Hex(c, "wage"),
                ExpiryDate = Hex(c, "expiryDate"),
                JoinedDate = Hex(c, "joinedDate"),
                TeamClub = Hex(c, "teamClub"),
                PlayingTime = OptHex(c, "playingTime"),
                Type = OptHex(c, "type"),
                Started = OptHex(c, "started"),
                LoyaltyBonus = OptHex(c, "loyaltyBonus"),
                SquadNumber = OptHex(c, "squadNumber"),
                Happiness = OptHex(c, "happiness"),
                PlayingTimeHappiness = OptHex(c, "playingTimeHappiness"),
                FuturePlayingTime = OptHex(c, "futurePlayingTime"),
                FuturePlayingTimeYear = OptHex(c, "futurePlayingTimeYear"),
                JoinedClub = OptHex(c, "joinedClub"),
                Clauses = OptHex(c, "clauses"),
            } : null,
            Team = root.TryGetProperty("team", out var tm) ? new TeamOffsets
            {
                VtableRva = (uint)Hex(tm, "vtable"),
                PlayerTeam = Hex(tm, "playerTeam"),
                Squad = Hex(tm, "squad"),
            } : null,
            PendingContract = root.TryGetProperty("pendingContract", out var pc) ? new PendingContractOffsets
            {
                PlayerPointer = Hex(pc, "playerPointer"),
                Clauses = OptHex(pc, "clauses"),
            } : null,
            Club = root.TryGetProperty("club", out var k) ? new ClubOffsets
            {
                Lists = k.TryGetProperty("lists", out var lists) ? ClubListOffsets.Load(lists) : null,
                FinanceLists = k.TryGetProperty("financeLists", out var financeLists) ? ClubFinanceListOffsets.Load(financeLists) : null,
                ClubVtableRva = (uint)Hex(k, "vtable"),
                Name = Hex(k, "name"),
                ShortName = Hex(k, "shortName"),
                Id = OptHex(k, "id"),
                Finance = k.TryGetProperty("finance", out var fi) ? new ClubFinanceOffsets
                {
                    Pointer = Hex(fi, "pointer"),
                    VtableRva = (uint?)OptHex(fi, "vtable"),
                    CompactVtableRva = (uint?)OptHex(fi, "compactVtable"),
                    CompactSize = OptHex(fi, "compactSize"),
                    Balance = OptHex(fi, "balance"),
                    TransferBudget = OptHex(fi, "transferBudget"),
                    SpentThisSeason = OptHex(fi, "spentThisSeason"),
                    WageSpend = OptHex(fi, "wageSpend"),
                    WageBill = OptHex(fi, "wageBill"),
                    TransferSpendMonth = OptHex(fi, "transferSpendMonth"),
                    PlayersSold = OptHex(fi, "playersSold"),
                } : null,
                TransferClauses = k.TryGetProperty("transferClauses", out var tc) ? new TransferClauseOffsets
                {
                    Pointer = Hex(tc, "pointer"),
                    SellOnVtableRva = (uint)Hex(tc, "sellOnVtable"),
                    AppearanceFeeVtableRva = (uint?)OptHex(tc, "appearanceFeeVtable"),
                    CompetitionBonusVtableRva = (uint?)OptHex(tc, "competitionBonusVtable"),
                } : null,
                Fields = k.TryGetProperty("fields", out var cf) ? ClubFields(cf) : null,
            } : null,
            GameDate = root.TryGetProperty("gameDate", out var g) ? new GameDateOffsets
            {
                Primary = Chain(g.GetProperty("primary")),
                CrossCheck = g.TryGetProperty("crossCheck", out var x) ? Chain(x) : null,
            } : null,
            Nation = root.TryGetProperty("nation", out var nat) ? new NationOffsets
            {
                PlayerNation = Hex(nat, "playerNation"),
                VtableRva = (uint)Hex(nat, "vtable"),
                Name = Hex(nat, "name"),
                ShortName = Hex(nat, "shortName"),
            } : null,
            Competition = root.TryGetProperty("competition", out var comp) ? new CompetitionOffsets
            {
                TeamCompetition = Hex(comp, "teamCompetition"),
                VtableRva = (uint)Hex(comp, "vtable"),
                Name = Hex(comp, "name"),
                ShortName = Hex(comp, "shortName"),
                Index = OptHex(comp, "index"),
                Id = OptHex(comp, "id"),
            } : null,
        };
    }

    static IReadOnlyDictionary<Clubs.ClubField, Clubs.ClubFieldOffset> ClubFields(JsonElement obj)
    {
        var map = new Dictionary<Clubs.ClubField, Clubs.ClubFieldOffset>();
        foreach (var p in obj.EnumerateObject())
        {
            if (!Enum.TryParse<Clubs.ClubField>(p.Name, out var field)) throw new FormatException($"Unknown club field '{p.Name}'");
            var v = p.Value;
            string Str(string name) => v.TryGetProperty(name, out var s) ? s.GetString() ?? "" : throw new FormatException($"{p.Name}: {name} is missing");
            map[field] = new Clubs.ClubFieldOffset(
                Enum.Parse<Clubs.ClubFieldRoot>(Str("root"), ignoreCase: true),
                Clubs.ClubFieldOffset.ParsePath(Str("path")),
                Enum.Parse<Clubs.ClubFieldType>(Str("type"), ignoreCase: true),
                v.TryGetProperty("scale", out var sc) ? sc.GetInt32() : 1,
                v.TryGetProperty("objectVtables", out var ov) ? ov.EnumerateArray().Select(x => (uint)ParseHex(x.GetString())).ToArray() : null);
        }
        return map;
    }

    static DateChain Chain(JsonElement e) =>
        new((uint)Hex(e, "rootRva"),
            e.TryGetProperty("pointerOffset", out _) ? Hex(e, "pointerOffset") : null,
            Hex(e, "dateOffset"),
            e.TryGetProperty("objectVtable", out _) ? (uint)Hex(e, "objectVtable") : null);

    public static OffsetProfile LoadFile(string path) => Load(File.ReadAllText(path));

    /// <summary>Same shape that <see cref="Load"/> reads, offsets written as hex strings.</summary>
    public string ToJson()
    {
        var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            void H(string name, long v) => w.WriteString(name, $"0x{v:X}");
            void Map<T>(string name, IReadOnlyDictionary<T, int> m) where T : struct, Enum
            {
                w.WriteStartObject(name);
                foreach (var (k, v) in m.OrderBy(kv => kv.Value)) H(k.ToString(), v);
                w.WriteEndObject();
            }
            var f = Fields;
            w.WriteStartObject();
            w.WriteString("build", Build);
            w.WriteString("module", Module);
            H("buildStringRva", BuildStringRva);
            w.WriteStartObject("playerList");
            H("rootRva", PlayerListRootRva); H("beginOffset", PlayerListBeginOffset); H("endOffset", PlayerListEndOffset);
            w.WriteEndObject();
            w.WriteStartArray("playerVtables");
            foreach (var v in PlayerVtableRvas) w.WriteStringValue($"0x{v:X}");
            w.WriteEndArray();
            w.WriteStartObject("player");
            H("objectSize", PlayerObjectSize);
            w.WriteNumber("attributeScale", AttributeScale);
            Map("attributes", Attributes);
            Map("positions", Positions);
            Map("personality", Personality);
            H("currentAbility", f.CurrentAbility); H("potentialAbility", f.PotentialAbility);
            H("homeReputation", f.HomeReputation); H("currentReputation", f.CurrentReputation); H("worldReputation", f.WorldReputation);
            H("height", f.Height); H("dateOfBirth", f.DateOfBirth);
            H("fullName", f.FullName); H("firstName", f.FirstName); H("lastName", f.LastName); H("commonName", f.CommonName);
            if (f.Id is { } pid) H("id", pid);
            w.WriteEndObject();
            if (PendingContract is { } pc)
            {
                w.WriteStartObject("pendingContract");
                H("playerPointer", pc.PlayerPointer);
                if (pc.Clauses is { } cl) H("clauses", cl);
                w.WriteEndObject();
            }
            if (Contract is { } c)
            {
                w.WriteStartObject("contract");
                H("playerContract", c.PlayerContract); H("vtable", c.ContractVtableRva); H("team", c.Team);
                H("wage", c.Wage); H("expiryDate", c.ExpiryDate); H("joinedDate", c.JoinedDate); H("teamClub", c.TeamClub);
                void O(string name, int? v) { if (v is { } x) H(name, x); }
                O("playingTime", c.PlayingTime); O("type", c.Type); O("started", c.Started); O("loyaltyBonus", c.LoyaltyBonus);
                O("squadNumber", c.SquadNumber); O("happiness", c.Happiness); O("playingTimeHappiness", c.PlayingTimeHappiness);
                O("futurePlayingTime", c.FuturePlayingTime); O("futurePlayingTimeYear", c.FuturePlayingTimeYear);
                O("joinedClub", c.JoinedClub); O("clauses", c.Clauses);
                w.WriteEndObject();
            }
            if (Team is { } tm)
            {
                w.WriteStartObject("team");
                H("vtable", tm.VtableRva); H("playerTeam", tm.PlayerTeam); H("squad", tm.Squad);
                w.WriteEndObject();
            }
            if (Club is { } k)
            {
                w.WriteStartObject("club");
                k.Lists?.Write(w);
                k.FinanceLists?.Write(w);
                H("vtable", k.ClubVtableRva); H("name", k.Name); H("shortName", k.ShortName);
                if (k.Id is { } id) H("id", id);
                if (k.Finance is { } fi)
                {
                    w.WriteStartObject("finance");
                    H("pointer", fi.Pointer);
                    if (fi.VtableRva is { } fv) H("vtable", fv);
                    if (fi.CompactVtableRva is { } cv2) H("compactVtable", cv2);
                    if (fi.CompactSize is { } cs) H("compactSize", cs);
                    if (fi.Balance is { } b) H("balance", b);
                    if (fi.TransferBudget is { } tb) H("transferBudget", tb);
                    if (fi.SpentThisSeason is { } sp) H("spentThisSeason", sp);
                    if (fi.WageSpend is { } ws) H("wageSpend", ws);
                    if (fi.WageBill is { } wb) H("wageBill", wb);
                    if (fi.TransferSpendMonth is { } tsm) H("transferSpendMonth", tsm);
                    if (fi.PlayersSold is { } ps) H("playersSold", ps);
                    w.WriteEndObject();
                }
                if (k.TransferClauses is { } tc)
                {
                    w.WriteStartObject("transferClauses");
                    H("pointer", tc.Pointer); H("sellOnVtable", tc.SellOnVtableRva);
                    if (tc.AppearanceFeeVtableRva is { } av) H("appearanceFeeVtable", av);
                    if (tc.CompetitionBonusVtableRva is { } cv) H("competitionBonusVtable", cv);
                    w.WriteEndObject();
                }
                if (k.Fields is { Count: > 0 } fields)
                {
                    w.WriteStartObject("fields");
                    foreach (var (field, o) in fields.OrderBy(kv => kv.Key))
                    {
                        w.WriteStartObject(field.ToString());
                        w.WriteString("root", o.Root.ToString().ToLowerInvariant());
                        w.WriteString("path", o.PathText);
                        w.WriteString("type", o.Type.ToString().ToLowerInvariant());
                        if (o.Scale != 1) w.WriteNumber("scale", o.Scale);
                        if (o.ObjectVtables is { Count: > 0 } ov)
                        {
                            w.WriteStartArray("objectVtables");
                            foreach (var vt in ov) w.WriteStringValue($"0x{vt:X}");
                            w.WriteEndArray();
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            if (GameDate is { } gd)
            {
                void Chain(string name, DateChain c)
                {
                    w.WriteStartObject(name);
                    H("rootRva", c.RootRva);
                    if (c.ObjectVtableRva is { } vt) H("objectVtable", vt);
                    if (c.PointerOffset is { } po) H("pointerOffset", po);
                    H("dateOffset", c.DateOffset);
                    w.WriteEndObject();
                }
                w.WriteStartObject("gameDate");
                Chain("primary", gd.Primary);
                if (gd.CrossCheck is { } cc) Chain("crossCheck", cc);
                w.WriteEndObject();
            }
            if (Nation is { } nat)
            {
                w.WriteStartObject("nation");
                H("playerNation", nat.PlayerNation); H("vtable", nat.VtableRva); H("name", nat.Name); H("shortName", nat.ShortName);
                w.WriteEndObject();
            }
            if (Competition is { } comp)
            {
                w.WriteStartObject("competition");
                H("teamCompetition", comp.TeamCompetition); H("vtable", comp.VtableRva); H("name", comp.Name); H("shortName", comp.ShortName);
                if (comp.Index is { } ci) H("index", ci);
                if (comp.Id is { } cid) H("id", cid);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Every enum member must have an offset, so a missing field fails at load instead of reading garbage.</summary>
    static IReadOnlyDictionary<TEnum, int> EnumMap<TEnum>(JsonElement obj) where TEnum : struct, Enum
    {
        var map = new Dictionary<TEnum, int>();
        foreach (var p in obj.EnumerateObject())
        {
            if (!Enum.TryParse<TEnum>(p.Name, out var key)) throw new FormatException($"Unknown {typeof(TEnum).Name} '{p.Name}'");
            map[key] = ParseHex(p.Value.GetString());
        }
        var missing = Enum.GetValues<TEnum>().Where(k => !map.ContainsKey(k)).ToArray();
        if (missing.Length > 0) throw new FormatException($"Missing {typeof(TEnum).Name}: {string.Join(", ", missing)}");
        return map;
    }

    static int Hex(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? ParseHex(v.GetString()) : throw new FormatException($"{name} is missing");

    static int? OptHex(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? ParseHex(v.GetString()) : null;

    static int ParseHex(string? s)
    {
        if (s is null || !s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) throw new FormatException($"Expected hex string, got '{s}'");
        return int.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}

public sealed class PlayerFieldOffsets
{
    public required int CurrentAbility { get; init; }
    public required int PotentialAbility { get; init; }
    public required int HomeReputation { get; init; }
    public required int CurrentReputation { get; init; }
    public required int WorldReputation { get; init; }
    public required int Height { get; init; }
    /// <summary>int16 day of year (1-based) followed by int16 year.</summary>
    public required int DateOfBirth { get; init; }
    /// <summary>Pointer to a length-prefixed UTF-8 string.</summary>
    public required int FullName { get; init; }
    /// <summary>Pointer to a name entry whose first qword points to a length-prefixed UTF-8 string.</summary>
    public required int FirstName { get; init; }
    public required int LastName { get; init; }
    public required int CommonName { get; init; }
    /// <summary>int32 unique ID as FMRTE shows it; names are not unique (two Nacho Gil).</summary>
    public int? Id { get; init; }
}
