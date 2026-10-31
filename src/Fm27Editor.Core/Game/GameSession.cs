using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Game;

/// <summary>An attached, build-checked Football Manager 27 process.</summary>
public sealed class GameSession : IDisposable
{
    readonly WindowsProcessMemory _memory;

    public IProcessMemory Memory => _memory;
    public WindowsProcessMemory Process => _memory;
    public GameProcess Game { get; }
    public OffsetProfile Profile { get; }
    public ulong ModuleBase => Game.ModuleBase;
    public bool CanWrite => _memory.CanWrite;

    public PlayerRepository Players { get; }
    public PlayerWriter Writer { get; }
    public ContractWriter Contracts { get; }
    public Transfer.TransferService Transfers { get; }

    /// <summary>
    /// Clubs and their teams, taken from the given players (pass <see cref="PlayerRepository.LoadAll"/>). Fast;
    /// clubs without players are missing. Addresses are valid until the save is reloaded.
    /// </summary>
    public IReadOnlyList<Clubs.ClubEntry> LoadClubs(IEnumerable<PlayerRecord> players) =>
        Clubs.ClubDirectory.FromPlayers(_memory, players, Profile, Game.ModuleBase);

    /// <summary>Every club, including those without players: a full heap scan, about 15 s.</summary>
    public IReadOnlyList<Clubs.ClubEntry> ScanAllClubs() =>
        Clubs.ClubDirectory.Load(_memory, _memory.EnumerateWritableRegions(Game.ModuleBase, Game.ModuleEnd), Profile, Game.ModuleBase);

    /// <summary>Adds a bonus/clause to the contract, growing its list in the game process when it is full.</summary>
    public void AddContractTerm(PlayerRecord player, ContractDetails snapshot, short type, int value, short parameter) =>
        Contracts.AddTerm(player, snapshot, type, value, parameter, _memory.Allocate);

    /// <summary>Applies a transfer, allocating a new squad buffer in the game process when the destination squad is full.</summary>
    public void ApplyTransfer(Transfer.TransferPlan plan) => Transfers.Apply(plan, _memory.Allocate);

    /// <summary>Current in-game date, read fresh on every call; null if unknown.</summary>
    public DateOnly? ReadGameDate() => GameClock.Read(_memory, Profile, Game.ModuleBase);

    /// <summary>Balance/budgets of the club object; null when the profile has no finance offsets.</summary>
    /// <summary>Sell-on, appearance fee and competition bonus clauses on the club's players.</summary>
    public IReadOnlyList<Clubs.TransferClause> ReadTransferClauses(ulong clubAddress, IReadOnlyDictionary<int, CompetitionInfo>? competitions = null) =>
        new Clubs.TransferClauseReader(_memory, Profile, Game.ModuleBase, competitions).Read(clubAddress);

    public IReadOnlyDictionary<int, CompetitionInfo> LoadCompetitions(IEnumerable<PlayerRecord> players) =>
        Clubs.CompetitionDirectory.FromPlayers(_memory, Profile, players);

    public Clubs.TransferClauseWriter TransferClauses => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Single-value club and team fields mapped in the profile (club.fields).</summary>
    public Clubs.ClubFieldAccess ClubFields => new(_memory, Profile, Game.ModuleBase);

    // Public names and parameter names are the list-provider contract agreed with lane B (STATUS [B 21:40]).
    public IReadOnlyList<Clubs.ClubVisionEntry> ReadClubVision(ulong teamAddress, IReadOnlyList<Clubs.ClubEntry>? clubs = null) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadClubVision(teamAddress, clubs);
    public IReadOnlyList<Clubs.ClubAffiliationEntry> ReadAffiliations(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadAffiliations(clubAddress);
    public IReadOnlyList<Clubs.ClubRegionalDivisionEntry> ReadRegionalDivisions(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadRegionalDivisions(clubAddress);
    public IReadOnlyList<Clubs.ClubSponsorEntry> ReadSponsors(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadSponsors(clubAddress);
    public IReadOnlyList<Clubs.ClubDebtEntry> ReadDebts(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadDebts(clubAddress);

    /// <summary>New sell-on owed by <paramref name="club"/>; the record is allocated in the game process.</summary>
    public ulong AddSellOn(ulong club, int playerId, ulong beneficiary, int percent, int? signedFor = null) =>
        TransferClauses.AddSellOn(club, playerId, beneficiary, percent, _memory.Allocate, signedFor);

    public ClubFinance? ReadFinance(ulong clubAddress) => new ClubFinanceReader(_memory, Profile, Game.ModuleBase).Read(clubAddress);

    /// <summary>Clause table on the player's agreed-but-not-completed contract; empty when there is none.</summary>
    public IReadOnlyList<ContractClause> ReadClauses(PlayerRecord player) =>
        new PendingContractReader(_memory, Profile).ReadClauses(player.PendingContractAddress);

    GameSession(WindowsProcessMemory memory, GameProcess game, OffsetProfile profile)
    {
        _memory = memory;
        Game = game;
        Profile = profile;
        Players = new PlayerRepository(memory, profile, game.ModuleBase);
        Writer = new PlayerWriter(memory, profile, game.ModuleBase);
        Contracts = new ContractWriter(memory, profile, game.ModuleBase);
        Transfers = new Transfer.TransferService(memory, profile, game.ModuleBase);
    }

    /// <summary>
    /// Attaches to the running game and picks the profile whose build string is present at its
    /// own RVA. Throws <see cref="UnsupportedBuildException"/> when no profile matches.
    /// </summary>
    public static GameSession Attach(IEnumerable<OffsetProfile> profiles, bool writable)
    {
        var all = profiles.ToList();
        var games = GameProcess.LocateAll(all.FirstOrDefault()?.Module ?? GameProcess.DefaultModule);
        Exception? accessError = null;
        foreach (var game in games)
        {
            WindowsProcessMemory memory;
            try { memory = new WindowsProcessMemory(game.ProcessId, writable); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or MemoryAccessException)
            { accessError = ex; continue; }
            try
            {
                foreach (var profile in all)
                    if (ReadBuild(memory, game.ModuleBase + profile.BuildStringRva, profile.Build.Length) == profile.Build)
                        return new GameSession(memory, game, profile);
            }
            catch { memory.Dispose(); throw; }
            memory.Dispose();
        }
        if (accessError is not null) throw new GameNotRunningException($"Could not access a game process: {accessError.Message}");
        throw new UnsupportedBuildException("No running game matches a supported build. Run: Fm27Editor.Tools find-offsets");
    }

    static string? ReadBuild(IProcessMemory memory, ulong address, int length)
    {
        var bytes = new byte[length];
        return memory.TryRead(address, bytes) ? Encoding.ASCII.GetString(bytes) : null;
    }

    /// <summary>
    /// Profiles built into Core, plus any *.json in <paramref name="directory"/> when it exists.
    /// A file there replaces the built-in profile for the same build.
    /// </summary>
    public static IReadOnlyList<OffsetProfile> LoadProfiles(string directory)
    {
        var byBuild = new Dictionary<string, OffsetProfile>();
        foreach (var p in BuiltInProfiles()) byBuild[p.Build] = p;
        if (Directory.Exists(directory))
            foreach (var p in Directory.EnumerateFiles(directory, "*.json").Select(OffsetProfile.LoadFile)) byBuild[p.Build] = p;
        return byBuild.Values.ToList();
    }

    public static IReadOnlyList<OffsetProfile> BuiltInProfiles()
    {
        var asm = typeof(GameSession).Assembly;
        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("offsets/", StringComparison.Ordinal))
            .Select(n =>
            {
                using var reader = new StreamReader(asm.GetManifestResourceStream(n)!);
                return OffsetProfile.Load(reader.ReadToEnd());
            })
            .ToList();
    }

    public void Dispose() => _memory.Dispose();
}

public sealed class GameNotRunningException(string message) : Exception(message);
public sealed class UnsupportedBuildException(string message) : Exception(message);
