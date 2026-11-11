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
    /// A new block the game will own: a record, or the buffer of a grown vector. It comes from the game's own CRT
    /// (ucrtbase!calloc), not VirtualAllocEx, because the game frees these with ucrtbase!free; Work Permit add/remove
    /// was traced doing exactly that on 9/11 and crashed the game when the block sat outside its allocator.
    /// </summary>
    public ulong AllocateGameBlock(int size)
    {
        ulong at = RemoteCrtAllocator.Allocate(_memory, size);
        _memory.OnAllocate?.Invoke(at, size);
        return at;
    }

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
    public void AddContractTerm(PlayerRecord player, ContractDetails snapshot, short type, int value, short parameter, ulong futureContract = 0) =>
        (futureContract == 0 ? Contracts : FutureContract(futureContract)).AddTerm(player, snapshot, type, value, parameter, AllocateGameBlock);

    /// <summary>Applies a transfer, allocating a new squad buffer in the game process when the destination squad is full.</summary>
    public void ApplyTransfer(Transfer.TransferPlan plan) => Transfers.Apply(plan, AllocateGameBlock);

    Presets.AcceleratedDevelopment? _accelerated;

    /// <summary>"Accelerate Player Development"; one instance per session so the located code site is reused.</summary>
    public Presets.AcceleratedDevelopment AcceleratedDevelopment =>
        _accelerated ??= new(_memory, _memory.CanWrite ? _memory : null, Profile, Game.ModuleBase, Game.ModuleSize);

    /// <summary>Current in-game date, read fresh on every call; null if unknown.</summary>
    public DateOnly? ReadGameDate() => GameClock.Read(_memory, Profile, Game.ModuleBase);

    /// <summary>Balance/budgets of the club object; null when the profile has no finance offsets.</summary>
    /// <summary>Sell-on, appearance fee and competition bonus clauses on the club's players.</summary>
    Clubs.TransferClauseReader? _clauseReader;

    /// <summary>The reader is cached so its one-off clause scan is shared by every screen in the session.</summary>
    public IReadOnlyList<Clubs.TransferClause> ReadTransferClauses(ulong clubAddress, IReadOnlyDictionary<int, CompetitionInfo>? competitions = null) =>
        (_clauseReader ??= new Clubs.TransferClauseReader(_memory, Profile, Game.ModuleBase)).Read(clubAddress, competitions);

    /// <summary>Every clause on 27.0.1+ (scanned), for the player screens that filter by player id.</summary>
    public IReadOnlyList<Clubs.TransferClause> ReadAllTransferClauses(IReadOnlyDictionary<int, CompetitionInfo>? competitions = null) =>
        (_clauseReader ??= new Clubs.TransferClauseReader(_memory, Profile, Game.ModuleBase)).ReadAll(competitions);

    IReadOnlyDictionary<int, CompetitionInfo>? _competitionList;
    readonly object _competitionListGate = new();

    public IReadOnlyDictionary<int, CompetitionInfo> LoadCompetitions(IEnumerable<PlayerRecord> players)
    {
        var map = new Dictionary<int, CompetitionInfo>(Clubs.CompetitionDirectory.FromPlayers(_memory, Profile, players));
        // The player's division only names domestic leagues; competition bonuses often point at cups and Europe,
        // so merge the game's own competition list (its index is the value a clause stores at +0x24). The list is
        // read once per session: every tab asks for it, and re-reading 8,000 competitions per view made the app slow.
        foreach (var (index, info) in CompetitionList()) map.TryAdd(index, info);
        return map;
    }

    IReadOnlyDictionary<int, CompetitionInfo> CompetitionList()
    {
        if (_competitionList is { } cached) return cached;
        lock (_competitionListGate)
        {
            if (_competitionList is { } inside) return inside;
            var map = new Dictionary<int, CompetitionInfo>();
            try
            {
                foreach (var competition in new Competitions.CompetitionRepository(_memory, Profile, ModuleBase).LoadAll(() => Players.LoadAll()))
                    map.TryAdd(competition.Index, new CompetitionInfo(competition.Address, competition.DisplayName, competition.ShortName));
            }
            catch (Exception) { }
            return _competitionList = map;
        }
    }

    public Clubs.TransferClauseWriter TransferClauses => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Finances → Income Statement: the club's monthly statement blocks.</summary>
    public Clubs.ClubFinancialStatements FinancialStatements => new(_memory, Profile, ModuleBase);

    /// <summary>Player values beyond attributes (condition, sharpness, morale, asking price, caps...), profile player.fields.</summary>
    public PlayerFieldAccess PlayerFields => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Renew for N years from the current in-game date (Renew for 4 Years = 4).</summary>
    public void RenewContract(PlayerRecord player, int years) =>
        Contracts.RenewForYears(player, years, ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"));

    /// <summary>Competition registrations, formed-at-club and formed-at-country relations, read only.</summary>
    public PlayerStatuses ReadStatuses(PlayerRecord player) => new PlayerStatusReader(_memory, Profile, Game.ModuleBase).Read(player);

    /// <summary>Adds a relationship row, growing the vector in the game process when it is full.</summary>
    public void AddRelationship(PlayerRecord player, PlayerStatuses current, RelationshipKind kind, ulong target, int value, int reason, bool permanent) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).Add(player, current, kind, target, value, reason, permanent, AllocateGameBlock);

    /// <summary>Ticks "Retired From Int. Foot.", growing the relations vector in the game process when it is full.</summary>
    public void AddInternationalRetirement(PlayerRecord player, PlayerStatuses current) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddInternationalRetirement(player, current, AllocateGameBlock);

    // FMRTE's Information-page presets. Each reads the player's relations fresh and returns false when there is nothing to do.
    public bool TrainedAtCurrentClub(PlayerRecord player) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddTrainedAtClub(player, ReadStatuses(player), CurrentClub(player), AllocateGameBlock);

    /// <summary>The nation object of the country a club plays in (the profile's club nation field).</summary>
    public ulong NationOfClub(ulong club)
    {
        int offset = Profile.Club?.Nation ?? throw new InvalidOperationException("Profile has no club nation offset");
        return _memory.ReadUInt64(club + (ulong)offset);
    }

    public bool TrainedInCurrentNation(PlayerRecord player)
    {
        int offset = Profile.Club?.Nation ?? throw new InvalidOperationException("Profile has no club nation offset");
        ulong nation = _memory.ReadUInt64(CurrentClub(player) + (ulong)offset);
        return new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddTrainedAtNation(player, ReadStatuses(player), nation, AllocateGameBlock);
    }

    /// <summary>FMRTE's Remove Int. Withdrawal; returns how many rows were rewritten (0 when the player has none).</summary>
    public int RemoveInternationalWithdrawal(PlayerRecord player) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).RemoveInternationalWithdrawal(player);

    public bool RegisterForUefa(PlayerRecord player)
    {
        int offset = Profile.Team?.PlayerTeam ?? throw new InvalidOperationException("Profile has no player team offset");
        ulong team = _memory.ReadUInt64(player.Address + (ulong)offset);
        if (team == 0) throw new InvalidOperationException($"{player.DisplayName} has no team");
        return new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddUefaRegistration(player, ReadStatuses(player), team, AllocateGameBlock);
    }

    /// <summary>FMRTE's Register for Domestic: the same competition-registration row as UEFA with code 8, for the team the player plays for.</summary>
    public bool RegisterForDomestic(PlayerRecord player)
    {
        int offset = Profile.Team?.PlayerTeam ?? throw new InvalidOperationException("Profile has no player team offset");
        ulong team = _memory.ReadUInt64(player.Address + (ulong)offset);
        if (team == 0) throw new InvalidOperationException($"{player.DisplayName} has no team");
        return new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddDomesticRegistration(player, ReadStatuses(player), team, AllocateGameBlock);
    }

    /// <summary>Squad members of <paramref name="teamAddress"/> (the team's own list) that have no registration row of
    /// <paramref name="kind"/> for it — what a squad-wide Register would add.</summary>
    public IReadOnlyList<PlayerRecord> PlayersWithoutRegistration(ulong teamAddress, RegistrationKind kind, IReadOnlyList<PlayerRecord>? squad = null)
    {
        var writer = new RelationshipWriter(_memory, Profile, Game.ModuleBase);
        var list = new List<PlayerRecord>();
        foreach (var player in squad ?? ReadSquad(teamAddress))
            if (!writer.HasRegistration(ReadStatuses(player), teamAddress, (byte)kind)) list.Add(player);
        return list;
    }

    /// <summary>
    /// FM Studio's RegisterSquadForUefa/Domestic: appends the competition-registration row for the team to every squad member
    /// that lacks it, one guarded player at a time. Returns how many rows were added.
    /// </summary>
    public int RegisterTeam(ulong teamAddress, RegistrationKind kind, IReadOnlyList<PlayerRecord>? squad = null)
    {
        var writer = new RelationshipWriter(_memory, Profile, Game.ModuleBase);
        int added = 0;
        foreach (var player in squad ?? ReadSquad(teamAddress))
        {
            var statuses = ReadStatuses(player);
            if (writer.HasRegistration(statuses, teamAddress, (byte)kind)) continue;
            bool ok = kind == RegistrationKind.Uefa
                ? writer.AddUefaRegistration(player, statuses, teamAddress, AllocateGameBlock)
                : writer.AddDomesticRegistration(player, statuses, teamAddress, AllocateGameBlock);
            if (ok) added++;
        }
        return added;
    }

    public bool SetTeamCohesion(PlayerRecord player, sbyte value) =>
        new TeamCohesionWriter(_memory, Profile, Game.ModuleBase).Set(player, CurrentClub(player), value);

    /// <summary>All-in-one, as FMRTE 27 defines it: Heal, Inspire, match bans removed, maximum tactical familiarity.</summary>
    public void AllInOne(PlayerRecord player) =>
        new PlayerPresetRunner(_memory, Profile, Game.ModuleBase).AllInOne(player,
            ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"));

    /// <summary>FMRTE's Tactical Understanding preset: maximum tactical familiarity and maximum team cohesion.</summary>
    public void TacticalUnderstanding(PlayerRecord player) =>
        new PlayerPresetRunner(_memory, Profile, Game.ModuleBase).TacticalUnderstanding(player);

    /// <summary>The club the player plays for (what FMRTE's presets act on), the contract club when that is unknown.</summary>
    ulong CurrentClub(PlayerRecord player) =>
        PlayingClub.Of(_memory, Profile, Game.ModuleBase, player) is var club and not 0 ? club : throw new InvalidOperationException($"{player.DisplayName} has no club");

    /// <summary>Career Plans: reader (plans, item names, picker catalogue) and guarded writer.</summary>
    public CareerPlanReader CareerPlans => new(_memory, Profile, Game.ModuleBase);
    public CareerPlanWriter CareerPlanWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Transfer Proposals: the offers a player's id list names, and the guarded fee writer.</summary>
    TransferProposalReader? _proposals;
    /// <summary>One reader per session so its offer map (one heap scan) is reused; a new attach rescans.
    /// Stale entries are revalidated on every read, so freed offers read as absent rather than corrupt.</summary>
    public TransferProposalReader Proposals => _proposals ??= new(_memory, Profile, Game.ModuleBase);
    public TransferProposalWriter ProposalWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Contracts offered with transfer proposals, and future transfers (Confirmed offers with an agreed date).</summary>
    public FutureTransferReader FutureTransfers => new(_memory, Profile, Game.ModuleBase);
    public FutureTransferWriter FutureTransferWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Creating a future transfer needs the offer, contract and team offsets in the profile (both Game Pass and Steam have them).</summary>
    public bool CanCreateFutureTransfer =>
        Profile.FutureTransfers is not null && Profile.Proposals is not null && Profile.Contract is not null && Profile.Team is not null;

    /// <summary>Checks a new future transfer against the live objects without writing anything.</summary>
    public FutureTransferCreator.Plan PlanFutureTransfer(PlayerRecord player, ulong buyingTeam, int fee, DateOnly date, TransferTerms? terms = null) =>
        new FutureTransferCreator(_memory, Profile, Game.ModuleBase).Prepare(player, buyingTeam, fee, ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"), date, terms);

    /// <summary>Writes the planned future transfer, then reads it back; throws if the date or fee do not read back.</summary>
    public void ApplyFutureTransfer(FutureTransferCreator.Plan plan)
    {
        if (ReadGameDate() != plan.Today) throw new StaleObjectException("The game date changed; nothing was written");
        int offerId = new FutureTransferCreator(_memory, Profile, Game.ModuleBase).Apply(plan, size => AllocateGameBlock(size));
        var fresh = Players.Reload(plan.Player.Address) ?? throw new MemoryAccessException("The player can't be read after the write");
        var transfer = FutureTransfers.Read(fresh, Proposals.Read(fresh)).SingleOrDefault(t => t.Offer.Id == offerId);
        if (transfer is null || transfer.Date != plan.Date || transfer.Offer.Value != plan.Fee)
            throw new MemoryAccessException("The future transfer was written but did not read back as expected");
    }

    /// <summary>Takes a future transfer back out of the game, then reads the player again to confirm it is gone.</summary>
    public void RemoveFutureTransfer(PlayerRecord player, FutureTransfer transfer)
    {
        var remover = new FutureTransferRemover(_memory, Profile, Game.ModuleBase);
        remover.Apply(remover.Prepare(player, transfer));
        var fresh = Players.Reload(player.Address) ?? throw new MemoryAccessException("The player can't be read after the removal");
        if (FutureTransfers.Read(fresh, Proposals.Read(fresh)).Any(t => t.Offer.Id == transfer.Offer.Id))
            throw new MemoryAccessException("The future transfer is still there after the removal");
    }

    /// <summary>Clone Center: copies the chosen groups from one player onto another; returns the number of fields written.</summary>
    public int ClonePlayer(PlayerRecord source, PlayerRecord target, CloneParts parts) =>
        new PlayerCloner(_memory, Profile, Game.ModuleBase).Clone(source, target, parts);

    /// <summary>National squads (Swap National Call Up).</summary>
    public NationalCallUpReader CallUps => new(_memory, Profile, Game.ModuleBase);
    public NationalCallUpWriter CallUpWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>The full contract editor on a future transfer's contract (FMRTE's Future Contract tab).</summary>
    public ContractWriter FutureContract(ulong contract) => new(_memory, Profile, Game.ModuleBase, contract);

    public void AddCareerPlan(PlayerRecord player, IReadOnlyList<CareerPlan> current, CareerPlanFields fields) => AddCareerPlan(Owner(player), current, fields);

    public void AddCareerPlan(PersonOwner player, IReadOnlyList<CareerPlan> current, CareerPlanFields fields) =>
        CareerPlanWriter.Add(player, current, fields, AllocateGameBlock);

    /// <summary>Every city (City Of Birth choices) and the player's own; guarded City Of Birth / Nationality swaps.</summary>
    public CityRepository Cities => new(_memory, Profile, Game.ModuleBase);
    public CityWriter CityWriter => new(_memory, Profile, Game.ModuleBase);
    public CityLinks CityLinks => new(_memory, Profile, Game.ModuleBase);
    public PromiseStore Promises => new(_memory, Profile, Game.ModuleBase);
    public PlayerTacticalUnderstanding PlayerTactics => new(_memory, Profile, Game.ModuleBase);
    public PlayerOriginWriter Origin => new(_memory, Profile, Game.ModuleBase);

    /// <summary>The person object a relationship to <paramref name="player"/> points at.</summary>
    public ulong PersonOf(PlayerRecord player) =>
        player.Address + (ulong)(Profile.Relations?.PlayerPerson ?? throw new InvalidOperationException("Profile has no relation offsets"));

    /// <summary>The player's bans / unavailability records, read only.</summary>
    public IReadOnlyList<PlayerBan> ReadBans(PlayerRecord player) => new BanReader(_memory, Profile, Game.ModuleBase).Read(player);

    /// <summary>The player's languages, as FMRTE's Languages tab shows them.</summary>
    public IReadOnlyList<PlayerLanguage> ReadLanguages(PlayerRecord player) => new LanguageReader(_memory, Profile, Game.ModuleBase).Read(player);

    /// <summary>The owner of person-level lists (languages, relations) for a player or a staff member.</summary>
    public PersonOwner Owner(PlayerRecord player) => PersonOwner.Of(player, Profile);
    public PlayerStatuses ReadStatuses(PersonOwner owner) => new PlayerStatusReader(_memory, Profile, Game.ModuleBase).Read(owner);
    public void AddRelationship(PersonOwner owner, PlayerStatuses current, RelationshipKind kind, ulong target, int value, int reason, bool permanent) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).Add(owner, current, kind, target, value, reason, permanent, AllocateGameBlock);
    public void AddOtherNationality(PersonOwner owner, PlayerStatuses current, ulong nation, int info) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddOtherNationality(owner, current, nation, info, AllocateGameBlock);
    public PersonOwner Owner(Staff.StaffRecord staff) => PersonOwner.Of(staff, Profile);
    public IReadOnlyList<PlayerLanguage> ReadLanguages(PersonOwner owner) => new LanguageReader(_memory, Profile, Game.ModuleBase).Read(owner);
    public void AddLanguage(PersonOwner owner, IReadOnlyList<PlayerLanguage> current, LanguageEntry language, int value) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Add(owner, current, language, value, AllocateGameBlock);
    public void RemoveLanguage(PersonOwner owner, IReadOnlyList<PlayerLanguage> current, PlayerLanguage language) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Remove(owner, current, language);

    public IReadOnlyList<LanguageEntry> LanguageCatalogue(IEnumerable<PlayerRecord> players) => new LanguageReader(_memory, Profile, Game.ModuleBase).Catalogue(players);

    /// <summary>Adds a language, allocating its record (and a bigger vector when full) in the game process.</summary>
    public void AddLanguage(PlayerRecord player, IReadOnlyList<PlayerLanguage> current, LanguageEntry language, int value) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Add(player, current, language, value, AllocateGameBlock);

    public void RemoveLanguage(PlayerRecord player, IReadOnlyList<PlayerLanguage> current, PlayerLanguage language) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Remove(player, current, language);

    /// <summary>Remove bans (user's rule: match bans only; day and global bans stay). Returns how many were removed.</summary>
    public int RemoveMatchBans(PlayerRecord player) => new BanWriter(_memory, Profile, Game.ModuleBase).RemoveMatchBans(player);

    /// <summary>Adds a ban, allocating its record (and a bigger vector when full) in the game process.</summary>
    public void AddBan(PlayerRecord player, IReadOnlyList<PlayerBan> current, BanWriter.BanFields fields) =>
        new BanWriter(_memory, Profile, Game.ModuleBase).Add(player, current, fields, AllocateGameBlock);

    /// <summary>Every stadium (29 thousand on the test save) and guarded stadium writes.</summary>
    public Stadiums.StadiumRepository Stadiums => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionRepository Competitions => new(_memory, Profile, Game.ModuleBase);
    public Awards.AwardRepository Awards => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationLinks NationLinks => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationTeamReader NationTeams => new(_memory, Profile, Game.ModuleBase);
    public Awards.AwardWriter AwardWriter => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionWriter CompetitionWriter => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionPrizeMoneyAccess CompetitionPrizeMoney => new(_memory, Profile, Game.ModuleBase);
    public void SetCompetitionClassificationEnabled(Competitions.CompetitionClassificationPrizes snapshot, bool enabled) =>
        CompetitionPrizeMoney.SetClassificationEnabled(snapshot, enabled, AllocateGameBlock);
    public Competitions.CompetitionStandingsAccess CompetitionStandings => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionVarAccess CompetitionVar => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionSquadRulesAccess CompetitionSquadRules => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionDetailsAccess CompetitionDetails => new(_memory, Profile, Game.ModuleBase);
    public Stadiums.StadiumWriter StadiumWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Every nation, and its mapped nation / national-team fields.</summary>
    public Nations.NationRepository Nations => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationFieldAccess NationFields => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationRivalries NationRivalries => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationPreferences NationPreferences => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationEuroCoefficients NationEuroCoefficients => new(_memory, Profile, Game.ModuleBase);
    /// <summary>Agreements: cached so the region catalogue scan runs once per session.</summary>
    Nations.NationAgreements? _nationAgreements;
    public Nations.NationAgreements NationAgreements => _nationAgreements ??= new(_memory, Profile, Game.ModuleBase);
    public Nations.NationColourAccess NationColours => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationKits NationKits => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationSquads NationSquads => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationStaff NationStaff => new(_memory, Profile, Game.ModuleBase);
    /// <summary>The contract a staff member holds with a national team, apart from his club contract.</summary>
    public Nations.NationStaffContracts NationStaffContracts => new(_memory, Profile, Game.ModuleBase);
    public Players.PlayerFamiliarityAccess PlayerFamiliarity => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Non-player staff, from the game's person vector (~23 thousand on the test save).</summary>
    public Staff.StaffRepository Staff => new(_memory, Profile, Game.ModuleBase);
    public Staff.StaffWriter StaffWriter => new(_memory, Profile, Game.ModuleBase);
    public Staff.StaffKnowledge StaffKnowledge => new(_memory, Profile, Game.ModuleBase);

    public void AddStaffKnowledge(Staff.StaffRecord staff, IReadOnlyList<Staff.StaffKnowledgeRow> current, ulong team, int value) =>
        StaffKnowledge.Add(staff, current, team, value, AllocateGameBlock);
    public Staff.StaffTendencies StaffTendencies => new(_memory, Profile, Game.ModuleBase);
    public Staff.StaffFormations StaffFormations => new(_memory, Profile, Game.ModuleBase);
    public Staff.StaffContractAccess StaffContracts => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Every unhappiness motive in the player's contract (ended ones too), read only.</summary>
    public IReadOnlyList<PlayerUnhappiness> ReadUnhappiness(PlayerRecord player) =>
        new UnhappinessReader(_memory, Profile, Game.ModuleBase).Read(player);

    /// <summary>Removes the active unhappiness motives (all when <paramref name="which"/> is null). Returns how many.</summary>
    public int RemoveUnhappiness(PlayerRecord player, Func<PlayerUnhappiness, bool>? which = null) =>
        new UnhappinessWriter(_memory, Profile, Game.ModuleBase).RemoveActive(player,
            ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"), which);

    /// <summary>The clubs refused a work permit for the player (FMRTE's Work Permit Denied list).</summary>
    public IReadOnlyList<WorkPermitDenial> ReadWorkPermitDenied(PlayerRecord player) =>
        new WorkPermitAccess(_memory, Profile, Game.ModuleBase).ReadDenied(player, team => TransferDeals.TeamName(team));

    /// <summary>The permits given to the player (FMRTE's Work Permit card).</summary>
    public IReadOnlyList<WorkPermitGrant> ReadWorkPermits(PlayerRecord player) =>
        new WorkPermitTable(_memory, Profile, Game.ModuleBase).Read(player, NationName);

    public void AddWorkPermit(PlayerRecord player, ulong nation, int year) =>
        new WorkPermitTable(_memory, Profile, Game.ModuleBase).Add(player, nation, year,
            size => AllocateGameBlock(size));

    public void RemoveWorkPermit(PlayerRecord player, WorkPermitGrant grant) =>
        new WorkPermitTable(_memory, Profile, Game.ModuleBase).Remove(player, grant, NationName);

    public void MakeWorkPermitPermanent(PlayerRecord player, WorkPermitGrant grant) =>
        new WorkPermitTable(_memory, Profile, Game.ModuleBase).MakePermanent(player, grant);

    public void SetWorkPermitYear(PlayerRecord player, WorkPermitGrant grant, int year) =>
        new WorkPermitTable(_memory, Profile, Game.ModuleBase).SetYear(player, grant, year);

    string? NationName(ulong nation) => Nations.LoadAll().FirstOrDefault(n => n.Address == nation)?.DisplayName;

    public bool RemoveWorkPermitDenied(PlayerRecord player, WorkPermitDenial denial) =>
        new WorkPermitAccess(_memory, Profile, Game.ModuleBase).RemoveDenied(player, denial);

    /// <summary>Grants a permit from <paramref name="nation"/> for <paramref name="year"/> to every
    /// player in <paramref name="players"/> that can take one; stale and duplicate players are
    /// skipped with reasons. Returns how many grants were made. One history group undoes it all.
    /// Progress reports (done, total) after each player.</summary>
    public int BatchGrantWorkPermits(IEnumerable<PlayerRecord> players, ulong nation, int year,
        Func<ulong, string?> nationName, Action<WorkPermitBatchSkip>? onSkip = null,
        Action<int, int>? progress = null)
    {
        var plan = WorkPermitBatch.Plan(_memory, Profile, Game.ModuleBase, players, nation, year, nationName);
        return WorkPermitBatch.Apply(plan, _memory, Profile, Game.ModuleBase,
            size => AllocateGameBlock(size), onSkip, null, progress);
    }

    InjuryWriter InjuryWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>FMRTE's "+" on the Injury tab: a new injury of <paramref name="type"/> starting on the in-game date.</summary>
    public PlayerInjury AddInjury(PlayerRecord player, PlayerInjury template, InjuryType type, InjuryTreatment treatment) =>
        InjuryWriter.Add(player, template, type, ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"), treatment, AllocateGameBlock);

    public void RemoveInjury(PlayerRecord player, PlayerInjury injury) => InjuryWriter.Remove(player, injury);

    /// <summary>Any injury in the game, to build new ones from (a player's own first, otherwise the first one found among <paramref name="players"/>).</summary>
    public PlayerInjury? InjuryTemplate(PlayerRecord player, IEnumerable<PlayerRecord> players)
    {
        if (ReadInjuries(player) is { Count: > 0 } own) return own[0];
        foreach (var p in players)
            if (ReadInjuries(p) is { Count: > 0 } found) return found[0];
        return null;
    }
    /// <summary>The fields of the injury types, as FMRTE's Injury page shows them.</summary>
    public InjuryTypeAccess InjuryTypeFields => new(_memory, Profile, Game.ModuleBase);

    IReadOnlyList<InjuryType>? _injuryTypes;
    /// <summary>Every kind of injury in the game, found once per connection (a heap scan).</summary>
    public IReadOnlyList<InjuryType> InjuryTypes(PlayerInjury? sample = null) => _injuryTypes ??= new InjuryReader(_memory, Profile, Game.ModuleBase).Types(sample);

    /// <summary>The player's injuries (empty when healthy), read only.</summary>
    public IReadOnlyList<PlayerInjury> ReadInjuries(PlayerRecord player) =>
        new InjuryReader(_memory, Profile, Game.ModuleBase).Read(player);

    /// <summary>
    /// Heal, as FMRTE 27's preset defines it: remove every injury, Condition (= FMRTE's Match Exercise) and Match
    /// Sharpness to 10000, Jadedness to 0. Each step is guarded; a failure stops before the remaining steps.
    /// </summary>
    public void Heal(PlayerRecord player) => new PlayerHealer(_memory, Profile, Game.ModuleBase).Heal(player);

    /// <summary>Inspire, as FMRTE 27 defines it: morale 20, jadedness 0, motives ended, contract happiness and
    /// playing-time happiness 100, team cohesion 100.</summary>
    public void Inspire(PlayerRecord player) =>
        new PlayerInspirer(_memory, Profile, Game.ModuleBase).Inspire(player,
            ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"));

    /// <summary>Destroy, as FMRTE 27 defines it: the shared writers set morale/condition/sharpness/jadedness, contract
    /// happiness, playing-time happiness and team cohesion; tactical familiarity is left alone.</summary>
    public void Destroy(PlayerRecord player) => new PlayerDestroyer(_memory, Profile, Game.ModuleBase).Destroy(player);

    /// <summary>Golden Generation for one player (PA, CA, Professionalism, Ambition); returns the values written.</summary>
    public Fm27Editor.Core.Players.GoldenGenerationValues GoldenGeneration(PlayerRecord player, int minPa = Fm27Editor.Core.Players.GoldenGenerationWriter.DefaultMinPa, int maxPa = Fm27Editor.Core.Players.GoldenGenerationWriter.DefaultMaxPa) =>
        new Fm27Editor.Core.Players.GoldenGenerationWriter(_memory, Profile, Game.ModuleBase).Apply(player, minPa, maxPa);

    /// <summary>Make it Expire: the contract expires on the current in-game date.</summary>
    public void ExpireContract(PlayerRecord player) =>
        Contracts.MakeExpire(player, ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"));

    /// <summary>Players of a team (from <see cref="LoadClubs"/> → <c>ClubEntry.Teams</c>) in squad order, freshly read.</summary>
    public IReadOnlyList<PlayerRecord> ReadSquad(ulong teamAddress)
    {
        var squad = new Clubs.SquadReader(_memory, Profile, Game.ModuleBase).Read(teamAddress);
        // Ages need the in-game date, which a fresh read does not carry (lane D found the Age column empty).
        if (ReadGameDate() is { } today)
            foreach (var p in squad) p.GameDate = today;
        return squad;
    }

    /// <summary>Single-value club and team fields mapped in the profile (club.fields).</summary>
    public Clubs.ClubFieldAccess ClubFields => new(_memory, Profile, Game.ModuleBase);

    // Public names and parameter names are the list-provider contract agreed with lane B (STATUS [B 21:40]).
    public IReadOnlyDictionary<int, string> ScanCompetitionNames() =>
        new Clubs.CompetitionNameIndex(_memory, Profile, ModuleBase).All;

    public IReadOnlyList<Clubs.ClubVisionEntry> ReadClubVision(ulong teamAddress, IReadOnlyList<Clubs.ClubEntry>? clubs = null,
        IReadOnlyDictionary<int, string>? competitionNames = null,
        IReadOnlyDictionary<int, string>? nationNames = null,
        IReadOnlyDictionary<int, string>? continentNames = null) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadClubVision(teamAddress, clubs, competitionNames, nationNames, continentNames);
    public Clubs.ClubVisionWriter ClubVisionWriter => new(_memory, Profile, Game.ModuleBase);
    public Clubs.ClubKits ClubKits => new(_memory, Profile, Game.ModuleBase);

    public void SetClubVisionImportance(ulong teamAddress, Clubs.ClubVisionEntry entry, int importance) =>
        new Clubs.ClubVisionWriter(_memory, Profile, ModuleBase).SetImportance(teamAddress, entry, importance);
    public IReadOnlyList<Clubs.ClubAffiliationEntry> ReadAffiliations(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadAffiliations(clubAddress);
    public void SetAffiliationAnnualFee(ulong clubAddress, Clubs.ClubAffiliationEntry entry, int fee) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetAnnualFee(clubAddress, entry, fee);
    public void SetAffiliationEndDate(ulong clubAddress, Clubs.ClubAffiliationEntry entry, DateOnly date) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetEndDate(clubAddress, entry, date);
    public void SetAffiliationStartDate(ulong clubAddress, Clubs.ClubAffiliationEntry entry, DateOnly date) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetStartDate(clubAddress, entry, date);
    public void SetAffiliationFriendlyChance(ulong clubAddress, Clubs.ClubAffiliationEntry entry, int value) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetFriendlyChance(clubAddress, entry, value);
    public void SetAffiliationParentWagePercent(ulong clubAddress, Clubs.ClubAffiliationEntry entry, int value) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetParentWagePercent(clubAddress, entry, value);
    public void SetAffiliationPlayFriendlies(ulong clubAddress, Clubs.ClubAffiliationEntry entry, bool value) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetPlayFriendlies(clubAddress, entry, value);
    public void SetAffiliationFirstOption(ulong clubAddress, Clubs.ClubAffiliationEntry entry, bool value) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetFirstOption(clubAddress, entry, value);
    public void SetAffiliationPlayersOnLoan(ulong clubAddress, Clubs.ClubAffiliationEntry entry, bool value) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetPlayersOnLoan(clubAddress, entry, value);
    public void SetAffiliationScoutingShared(ulong clubAddress, Clubs.ClubAffiliationEntry entry, bool value) =>
        new Clubs.ClubAffiliationWriter(_memory, Profile, ModuleBase).SetScoutingShared(clubAddress, entry, value);
    public IReadOnlyList<Clubs.ClubRegionalDivisionEntry> ReadRegionalDivisions(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadRegionalDivisions(clubAddress);
    public IReadOnlyList<Clubs.ClubSponsorEntry> ReadSponsors(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadSponsors(clubAddress);
    /// <summary>Transfer-deal (negotiation) objects; the save has none until an offer is open.</summary>
    public Clubs.TransferDealReader TransferDeals => new(_memory, Profile, ModuleBase);
    public Clubs.TransferDealWriter TransferDealWriter => new(_memory, Profile, ModuleBase);
    public Players.CurrentLoanReader CurrentLoans => new(_memory, Profile, ModuleBase);
    public Players.CurrentLoanInspection ReadCurrentLoan(Players.PlayerRecord player) => CurrentLoans.Read(player);

    /// <summary>A nation's 20 transfer-value tiers (men or women), guarded write.</summary>
    public Nations.NationTransferValuesReader NationTransferValues => new(_memory);
    public Nations.NationTransferValues? ReadNationTransferValues(Nations.NationRecord nation, bool women = false) =>
        NationTransferValues.Read(nation.Address, women);
    public void SetNationTransferValue(Nations.NationTransferValues snapshot, int index, int value) =>
        new Nations.NationTransferValuesWriter(_memory).Set(snapshot, index, value);
    public void SetNationTransferValues(Nations.NationRecord nation, Nations.NationTransferValues snapshot, IReadOnlyDictionary<int, int> values) =>
        new Nations.NationTransferValuesWriter(_memory).SetMany(nation.Address,
            ModuleBase + (Profile.Nation?.VtableRva ?? throw new InvalidOperationException("No nation vtable")),
            ModuleBase + (Profile.Nation?.Preferences?.TeamVtable ?? throw new InvalidOperationException("No national team vtable")), snapshot, values);
    /// <summary>Referees from the game-wide officials array (found through the clubs anchor).</summary>
    public Officials.RefereeDirectory Referees => new(_memory, ModuleBase, Game.ModuleEnd - ModuleBase);
    public IReadOnlyList<Officials.RefereeRecord> ReadReferees() => Referees.ReadAll();
    public void SetReferee(Officials.RefereeRecord referee, Officials.RefereeField field, int value) =>
        new Officials.RefereeWriter(_memory, ModuleBase).Set(referee, field, value);
    /// <summary>Term editing and recall for a player who is out on loan.</summary>
    public Players.LoanEditor Loans => new(_memory, Profile, Game.ModuleBase);
    public Players.ActiveLoan? ReadLoan(Players.PlayerRecord player) => Loans.Read(player);
    public Players.LoanRecallPlan PlanLoanRecall(Players.PlayerRecord player) => Loans.PlanRecall(player);
    /// <summary>Recalls the player to his contract club: playing team, both squad vectors, loan holder cleared.</summary>
    public void RecallLoan(Players.PlayerRecord player) => Loans.ApplyRecall(Loans.PlanRecall(player), AllocateGameBlock);
    /// <summary>Applies a recall plan, allocating a new parent squad buffer in the game process when it is full.</summary>
    public void ApplyLoanRecall(Players.LoanRecallPlan plan) => Loans.ApplyRecall(plan, AllocateGameBlock);
    public void SetLoanTerms(Players.ActiveLoan loan, Players.LoanTermsEdit edit) => Loans.SetTerms(loan, edit);
    /// <summary>Loans a player out like FMRTE's Move → Loan: plan first, then <see cref="ApplyLoanCreate"/>.</summary>
    public Players.LoanCreator LoanCreation => new(_memory, Profile, Game.ModuleBase);
    public Players.LoanCreatePlan PlanLoanCreate(Players.PlayerRecord player, ulong borrowingTeam, Players.LoanOffer offer, IEnumerable<Players.PlayerRecord> players) =>
        LoanCreation.Plan(player, borrowingTeam, offer, players);
    public void ApplyLoanCreate(Players.LoanCreatePlan plan) => LoanCreation.Apply(plan, AllocateGameBlock);

    /// <summary>Swaps two players between their clubs for no fee; both legs are applied with fresh reads in between.</summary>
    public Transfer.SwapResult SwapPlayers(Players.PlayerRecord first, Players.PlayerRecord second) =>
        new Transfer.PlayerSwapService(_memory, Profile, Game.ModuleBase).Swap(first, second,
            () => Players.LoadAll(), () => LoadClubs(Players.LoadAll()), AllocateGameBlock);
    public Clubs.ClubSponsorWriter SponsorWriter => new(_memory, Profile, ModuleBase, AllocateGameBlock);
    public void SetSponsorTotal(ulong clubAddress, Clubs.ClubSponsorEntry entry, int value) =>
        SponsorWriter.SetTotal(clubAddress, entry, value);
    public void SetSponsorDates(ulong clubAddress, Clubs.ClubSponsorEntry entry, DateOnly start, DateOnly end) =>
        SponsorWriter.SetDates(clubAddress, entry, start, end);
    public void SetSponsorIncomeType(ulong clubAddress, Clubs.ClubSponsorEntry entry, byte value) =>
        SponsorWriter.SetIncomeType(clubAddress, entry, value);
    public void SetSponsorFlags(ulong clubAddress, Clubs.ClubSponsorEntry entry, bool renewIncome, bool fixedValue) =>
        SponsorWriter.SetFlags(clubAddress, entry, renewIncome, fixedValue);
    public IReadOnlyList<Clubs.ClubDebtEntry> ReadDebts(ulong clubAddress) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadDebts(clubAddress);
    public void SetDebtPayments(ulong clubAddress, Clubs.ClubDebtEntry entry, int repayment, int interest) =>
        new Clubs.ClubDebtWriter(_memory, Profile, ModuleBase).SetPayments(clubAddress, entry, repayment, interest);
    public void SetDebtInterestOnly(ulong clubAddress, Clubs.ClubDebtEntry entry, bool value) =>
        new Clubs.ClubDebtWriter(_memory, Profile, ModuleBase).SetInterestOnly(clubAddress, entry, value);
    /// <summary>Drops every debt of the club (Finances → Debt and Loans).</summary>
    public void RemoveAllDebts(ulong clubAddress) =>
        new Clubs.ClubDebtWriter(_memory, Profile, ModuleBase).RemoveAll(clubAddress);
    public void SetDebtSource(ulong clubAddress, Clubs.ClubDebtEntry entry, byte value) =>
        new Clubs.ClubDebtWriter(_memory, Profile, ModuleBase).SetSource(clubAddress, entry, value);
    public void SetOriginalDebt(ulong clubAddress, Clubs.ClubDebtEntry entry, int value) =>
        new Clubs.ClubDebtWriter(_memory, Profile, ModuleBase).SetOriginalDebt(clubAddress, entry, value);
    public void SetDebtDates(ulong clubAddress, Clubs.ClubDebtEntry entry, DateOnly start, DateOnly end) =>
        new Clubs.ClubDebtWriter(_memory, Profile, ModuleBase).SetDates(clubAddress, entry, start, end);

    public IReadOnlyList<Clubs.ClubRelationship> ReadClubRelationships(ulong clubAddress) =>
        new Clubs.ClubRelationshipReader(_memory, Profile, ModuleBase).Read(clubAddress);
    public void SetClubRelationship(ulong clubAddress, Clubs.ClubRelationship row, int? reason, bool? permanent, int? value) =>
        new Clubs.ClubRelationshipWriter(_memory, Profile, ModuleBase).SetDetails(clubAddress, row, reason, permanent, value);

    /// <summary>New sell-on owed by <paramref name="club"/>; the record is allocated in the game process.</summary>
    public ulong AddSellOn(ulong club, int playerId, ulong beneficiary, int percent, int? signedFor = null) =>
        TransferClauses.AddSellOn(club, playerId, beneficiary, percent, AllocateGameBlock, signedFor);

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
            try { memory = new WindowsProcessMemory(game.ProcessId, false); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or MemoryAccessException)
            { accessError = ex; continue; }
            try
            {
                bool store = game.ExePath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase);
                foreach (var profile in all.OrderBy(p => PreferredRank(p, store)))
                    if (ProfileMatches(memory, game.ModuleBase, game.ModuleSize, profile))
                    {
                        if (writable && !profile.ReadOnly)
                        {
                            var writableMemory = new WindowsProcessMemory(game.ProcessId, true);
                            memory.Dispose();
                            memory = writableMemory;
                        }
                        return new GameSession(memory, game, profile);
                    }
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

    /// <summary>Checks build string and optional PE fingerprint without dereferencing game data.</summary>
    /// <summary>
    /// Prefers the profile variant that belongs to the installed flavour: the Microsoft Store image ships different
    /// RVAs from the Steam one, while both carry the same build string.
    /// </summary>
    /// <summary>True when the profile's own list root reads as zero, i.e. the profile does not belong to this image.</summary>
    static bool RootLooksEmpty(IProcessMemory memory, ulong moduleBase, OffsetProfile profile)
    {
        if (profile.Competition?.ListRootRva is not { } rva) return false;
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(moduleBase + (ulong)rva, b) && BitConverter.ToUInt64(b) == 0;
    }

    static int PreferredRank(OffsetProfile profile, bool store)
    {
        bool gamePass = profile.Variant is { } v && v.Contains("gamepass", StringComparison.OrdinalIgnoreCase);
        return store == gamePass ? 0 : 1;
    }

    public static bool ProfileMatches(IProcessMemory memory, ulong moduleBase, int moduleSize, OffsetProfile profile)
    {
        var build = profile.MatchBuild ?? profile.Build;
        if (ReadBuild(memory, moduleBase + profile.BuildStringRva, build.Length) != build) return false;
        if (profile.ImageSize is { } size && (ulong)moduleSize != size) return false;
        if (profile.PeTimestamp is not { } expected) return true;
        Span<byte> header = stackalloc byte[64];
        if (!memory.TryRead(moduleBase, header) || header[0] != (byte)'M' || header[1] != (byte)'Z') return false;
        uint pe = BitConverter.ToUInt32(header[0x3C..]);
        if (pe > (uint)moduleSize || (uint)moduleSize - pe < 12) return false;
        Span<byte> signature = stackalloc byte[12];
        return memory.TryRead(moduleBase + pe, signature) && BitConverter.ToUInt32(signature) == 0x00004550 &&
               BitConverter.ToUInt32(signature[8..]) == expected;
    }

    /// <summary>
    /// Profiles built into Core, plus any *.json in <paramref name="directory"/> when it exists.
    /// A file there replaces the built-in profile for the same build.
    /// </summary>
    public static IReadOnlyList<OffsetProfile> LoadProfiles(string directory)
    {
        var byBuild = new Dictionary<string, OffsetProfile>();
        foreach (var p in BuiltInProfiles()) byBuild[p.Build + "|" + p.Variant] = p;
        if (Directory.Exists(directory))
            foreach (var p in Directory.EnumerateFiles(directory, "*.json").Select(OffsetProfile.LoadFile)) byBuild[p.Build + "|" + p.Variant] = p;
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
