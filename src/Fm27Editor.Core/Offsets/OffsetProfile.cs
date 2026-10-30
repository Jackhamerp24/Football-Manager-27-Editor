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
    public ClubOffsets? Club { get; init; }
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
            } : null,
            Club = root.TryGetProperty("club", out var k) ? new ClubOffsets
            {
                ClubVtableRva = (uint)Hex(k, "vtable"),
                Name = Hex(k, "name"),
                ShortName = Hex(k, "shortName"),
            } : null,
            GameDate = root.TryGetProperty("gameDate", out var g) ? new GameDateOffsets
            {
                Primary = Chain(g.GetProperty("primary")),
                CrossCheck = g.TryGetProperty("crossCheck", out var x) ? Chain(x) : null,
            } : null,
        };
    }

    static DateChain Chain(JsonElement e) =>
        new((uint)Hex(e, "rootRva"), Hex(e, "pointerOffset"), Hex(e, "dateOffset"));

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
            w.WriteEndObject();
            if (Contract is { } c)
            {
                w.WriteStartObject("contract");
                H("playerContract", c.PlayerContract); H("vtable", c.ContractVtableRva); H("team", c.Team);
                H("wage", c.Wage); H("expiryDate", c.ExpiryDate); H("joinedDate", c.JoinedDate); H("teamClub", c.TeamClub);
                w.WriteEndObject();
            }
            if (Club is { } k)
            {
                w.WriteStartObject("club");
                H("vtable", k.ClubVtableRva); H("name", k.Name); H("shortName", k.ShortName);
                w.WriteEndObject();
            }
            if (GameDate is { } gd)
            {
                void Chain(string name, DateChain c)
                {
                    w.WriteStartObject(name);
                    H("rootRva", c.RootRva); H("pointerOffset", c.PointerOffset); H("dateOffset", c.DateOffset);
                    w.WriteEndObject();
                }
                w.WriteStartObject("gameDate");
                Chain("primary", gd.Primary);
                if (gd.CrossCheck is { } cc) Chain("crossCheck", cc);
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
}
