using System.Text.Json;
using System.Text.Json.Serialization;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Transfer;

/// <summary>Raw bytes of one game object, kept as hex so two captures can be compared offline.</summary>
public sealed class ObjectDump
{
    public string Kind { get; set; } = "";
    public int Size { get; set; }
    public string Hex { get; set; } = "";

    public static ObjectDump? TryCapture(IProcessMemory memory, string kind, ulong address, int wanted)
    {
        if (address == 0) return null;
        foreach (var size in new[] { wanted, wanted / 2, wanted / 4, wanted / 8 })
        {
            if (size < 8) break;
            var buffer = new byte[size];
            if (!memory.TryRead(address, buffer)) continue;
            return new ObjectDump { Kind = kind, Size = size, Hex = Convert.ToHexString(buffer) };
        }
        return null;
    }
}

/// <summary>
/// Everything one player's transfer touches, captured at one moment: the player object, its completed
/// contract, the agreed-but-not-completed contract, the club object, the club's money, and the clause
/// table. Addresses are stored for reads but are not identity: they change on every save load.
/// </summary>
public sealed class TransferSnapshot
{
    public string Build { get; set; } = "";
    public string CapturedAt { get; set; } = "";
    public string? GameDate { get; set; }
    public string PlayerName { get; set; } = "";
    public ulong PlayerAddress { get; set; }
    public string? Club { get; set; }
    public int? Wage { get; set; }
    public string? Expires { get; set; }
    public string? Joined { get; set; }
    public ulong PendingContractAddress { get; set; }
    public List<ObjectDump> Objects { get; set; } = [];
    public List<ContractClause> Clauses { get; set; } = [];
    public ClubFinance? Finance { get; set; }

    public ObjectDump? Object(string kind) => Objects.FirstOrDefault(o => o.Kind == kind);

    public static TransferSnapshot Capture(GameSession session, PlayerRecord player) =>
        Capture(session.Memory, session.Profile, session.ModuleBase, player, session.ReadGameDate(),
            session.ReadFinance(player.Club?.Address ?? 0), session.ReadClauses(player));

    /// <summary>Same capture without a live session, so tests can drive it with a fake address space.</summary>
    public static TransferSnapshot Capture(
        IProcessMemory memory, Offsets.OffsetProfile profile, ulong moduleBase,
        PlayerRecord player, DateOnly? gameDate, ClubFinance? finance, IReadOnlyList<ContractClause> clauses)
    {
        var snapshot = new TransferSnapshot
        {
            Build = profile.Build,
            CapturedAt = DateTimeOffset.Now.ToString("O"),
            GameDate = gameDate?.ToString("yyyy-MM-dd"),
            PlayerName = player.DisplayName,
            PlayerAddress = player.Address,
            Club = player.Club?.DisplayName,
            Wage = player.Contract?.Wage,
            Expires = player.Contract?.Expires?.ToString("yyyy-MM-dd"),
            Joined = player.Contract?.Joined?.ToString("yyyy-MM-dd"),
            PendingContractAddress = player.PendingContractAddress,
            Clauses = [.. clauses],
            Finance = finance,
        };
        Add(snapshot, memory, "player", player.Address, profile.PlayerObjectSize);
        if (player.Contract is { } contract) Add(snapshot, memory, "contract", contract.Address, 0x80);
        Add(snapshot, memory, "pending", player.PendingContractAddress, 0x200);
        if (player.Club is { } club) Add(snapshot, memory, "club", club.Address, 0x170);
        return snapshot;
    }

    static void Add(TransferSnapshot snapshot, IProcessMemory memory, string kind, ulong address, int size)
    {
        if (ObjectDump.TryCapture(memory, kind, address, size) is { } dump) snapshot.Objects.Add(dump);
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, TransferJson.Default.TransferSnapshot));

    public static TransferSnapshot Load(string path) =>
        JsonSerializer.Deserialize<TransferSnapshot>(File.ReadAllText(path), TransferJson.Default.TransferSnapshot)
        ?? throw new FormatException($"Not a transfer snapshot: {path}");
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TransferSnapshot))]
internal sealed partial class TransferJson : JsonSerializerContext;
