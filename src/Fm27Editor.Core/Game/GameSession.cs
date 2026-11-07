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
    public void AddContractTerm(PlayerRecord player, ContractDetails snapshot, short type, int value, short parameter, ulong futureContract = 0) =>
        (futureContract == 0 ? Contracts : FutureContract(futureContract)).AddTerm(player, snapshot, type, value, parameter, _memory.Allocate);

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

    /// <summary>Finances → Income Statement: the club's monthly statement blocks.</summary>
    public Clubs.ClubFinancialStatements FinancialStatements => new(_memory, Profile);

    /// <summary>Player values beyond attributes (condition, sharpness, morale, asking price, caps...), profile player.fields.</summary>
    public PlayerFieldAccess PlayerFields => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Renew for N years from the current in-game date (Renew for 4 Years = 4).</summary>
    public void RenewContract(PlayerRecord player, int years) =>
        Contracts.RenewForYears(player, years, ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"));

    /// <summary>Competition registrations, formed-at-club and formed-at-country relations, read only.</summary>
    public PlayerStatuses ReadStatuses(PlayerRecord player) => new PlayerStatusReader(_memory, Profile, Game.ModuleBase).Read(player);

    /// <summary>Adds a relationship row, growing the vector in the game process when it is full.</summary>
    public void AddRelationship(PlayerRecord player, PlayerStatuses current, RelationshipKind kind, ulong target, int value, int reason, bool permanent) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).Add(player, current, kind, target, value, reason, permanent, _memory.Allocate);

    /// <summary>Ticks "Retired From Int. Foot.", growing the relations vector in the game process when it is full.</summary>
    public void AddInternationalRetirement(PlayerRecord player, PlayerStatuses current) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddInternationalRetirement(player, current, _memory.Allocate);

    // FMRTE's Information-page presets. Each reads the player's relations fresh and returns false when there is nothing to do.
    public bool TrainedAtCurrentClub(PlayerRecord player) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddTrainedAtClub(player, ReadStatuses(player), CurrentClub(player), _memory.Allocate);

    public bool TrainedInCurrentNation(PlayerRecord player)
    {
        int offset = Profile.Club?.Nation ?? throw new InvalidOperationException("Profile has no club nation offset");
        ulong nation = _memory.ReadUInt64(CurrentClub(player) + (ulong)offset);
        return new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddTrainedAtNation(player, ReadStatuses(player), nation, _memory.Allocate);
    }

    public bool RegisterForUefa(PlayerRecord player)
    {
        int offset = Profile.Team?.PlayerTeam ?? throw new InvalidOperationException("Profile has no player team offset");
        ulong team = _memory.ReadUInt64(player.Address + (ulong)offset);
        if (team == 0) throw new InvalidOperationException($"{player.DisplayName} has no team");
        return new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddUefaRegistration(player, ReadStatuses(player), team, _memory.Allocate);
    }

    public bool SetTeamCohesion(PlayerRecord player, sbyte value) =>
        new TeamCohesionWriter(_memory, Profile, Game.ModuleBase).Set(player, CurrentClub(player), value);

    /// <summary>
    /// FMRTE 27's All-in-one, as diffed on Kobbie Mainoo (2/11/2026): Heal, Morale 20, contract happiness and playing-time
    /// happiness 100, team cohesion 100. Steps already at their value are not written.
    /// </summary>
    public void AllInOne(PlayerRecord player)
    {
        Heal(player);
        Inspire(player);
        if (Contracts.Read(player) is { } c)
        {
            if (c.Happiness is { } h && h != 100) Contracts.SetHappiness(player, c, 100);
            if (Contracts.Read(player) is { PlayingTimeHappiness: { } ph } c2 && ph != 100) Contracts.SetPlayingTimeHappiness(player, c2, 100);
        }
        if (player.Club is not null) SetTeamCohesion(player, TeamCohesionWriter.Improve);
    }

    static ulong CurrentClub(PlayerRecord player) =>
        player.Club?.Address is { } club and not 0 ? club : throw new InvalidOperationException($"{player.DisplayName} has no club");

    /// <summary>Career Plans: reader (plans, item names, picker catalogue) and guarded writer.</summary>
    public CareerPlanReader CareerPlans => new(_memory, Profile, Game.ModuleBase);
    public CareerPlanWriter CareerPlanWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Transfer Proposals: the offers a player's id list names, and the guarded fee writer.</summary>
    public TransferProposalReader Proposals => new(_memory, Profile, Game.ModuleBase);
    public TransferProposalWriter ProposalWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Contracts offered with transfer proposals, and future transfers (Confirmed offers with an agreed date).</summary>
    public FutureTransferReader FutureTransfers => new(_memory, Profile, Game.ModuleBase);
    public FutureTransferWriter FutureTransferWriter => new(_memory, Profile, Game.ModuleBase);

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
        CareerPlanWriter.Add(player, current, fields, _memory.Allocate);

    /// <summary>Every city (City Of Birth choices) and the player's own; guarded City Of Birth / Nationality swaps.</summary>
    public CityRepository Cities => new(_memory, Profile, Game.ModuleBase);
    public CityWriter CityWriter => new(_memory, Profile, Game.ModuleBase);
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
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).Add(owner, current, kind, target, value, reason, permanent, _memory.Allocate);
    public void AddOtherNationality(PersonOwner owner, PlayerStatuses current, ulong nation, int info) =>
        new RelationshipWriter(_memory, Profile, Game.ModuleBase).AddOtherNationality(owner, current, nation, info, _memory.Allocate);
    public PersonOwner Owner(Staff.StaffRecord staff) => PersonOwner.Of(staff, Profile);
    public IReadOnlyList<PlayerLanguage> ReadLanguages(PersonOwner owner) => new LanguageReader(_memory, Profile, Game.ModuleBase).Read(owner);
    public void AddLanguage(PersonOwner owner, IReadOnlyList<PlayerLanguage> current, LanguageEntry language, int value) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Add(owner, current, language, value, _memory.Allocate);
    public void RemoveLanguage(PersonOwner owner, IReadOnlyList<PlayerLanguage> current, PlayerLanguage language) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Remove(owner, current, language);

    public IReadOnlyList<LanguageEntry> LanguageCatalogue(IEnumerable<PlayerRecord> players) => new LanguageReader(_memory, Profile, Game.ModuleBase).Catalogue(players);

    /// <summary>Adds a language, allocating its record (and a bigger vector when full) in the game process.</summary>
    public void AddLanguage(PlayerRecord player, IReadOnlyList<PlayerLanguage> current, LanguageEntry language, int value) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Add(player, current, language, value, _memory.Allocate);

    public void RemoveLanguage(PlayerRecord player, IReadOnlyList<PlayerLanguage> current, PlayerLanguage language) =>
        new LanguageWriter(_memory, Profile, Game.ModuleBase).Remove(player, current, language);

    /// <summary>Remove bans (user's rule: match bans only; day and global bans stay). Returns how many were removed.</summary>
    public int RemoveMatchBans(PlayerRecord player) => new BanWriter(_memory, Profile, Game.ModuleBase).RemoveMatchBans(player);

    /// <summary>Adds a ban, allocating its record (and a bigger vector when full) in the game process.</summary>
    public void AddBan(PlayerRecord player, IReadOnlyList<PlayerBan> current, BanWriter.BanFields fields) =>
        new BanWriter(_memory, Profile, Game.ModuleBase).Add(player, current, fields, _memory.Allocate);

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
        CompetitionPrizeMoney.SetClassificationEnabled(snapshot, enabled, _memory.Allocate);
    public Competitions.CompetitionStandingsAccess CompetitionStandings => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionVarAccess CompetitionVar => new(_memory, Profile, Game.ModuleBase);
    public Competitions.CompetitionSquadRulesAccess CompetitionSquadRules => new(_memory, Profile, Game.ModuleBase);
    public Stadiums.StadiumWriter StadiumWriter => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Every nation, and its mapped nation / national-team fields.</summary>
    public Nations.NationRepository Nations => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationFieldAccess NationFields => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationRivalries NationRivalries => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationPreferences NationPreferences => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationEuroCoefficients NationEuroCoefficients => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationAgreements NationAgreements => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationColourAccess NationColours => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationKits NationKits => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationSquads NationSquads => new(_memory, Profile, Game.ModuleBase);
    public Nations.NationStaff NationStaff => new(_memory, Profile, Game.ModuleBase);
    public Players.PlayerFamiliarityAccess PlayerFamiliarity => new(_memory, Profile, Game.ModuleBase);

    /// <summary>Non-player staff, from the game's person vector (~23 thousand on the test save).</summary>
    public Staff.StaffRepository Staff => new(_memory, Profile, Game.ModuleBase);
    public Staff.StaffWriter StaffWriter => new(_memory, Profile, Game.ModuleBase);
    public Staff.StaffKnowledge StaffKnowledge => new(_memory, Profile, Game.ModuleBase);

    public void AddStaffKnowledge(Staff.StaffRecord staff, IReadOnlyList<Staff.StaffKnowledgeRow> current, ulong team, int value) =>
        StaffKnowledge.Add(staff, current, team, value, _memory.Allocate);
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

    /// <summary>The player's injuries (empty when healthy), read only.</summary>
    public IReadOnlyList<PlayerInjury> ReadInjuries(PlayerRecord player) =>
        new InjuryReader(_memory, Profile, Game.ModuleBase).Read(player);

    /// <summary>
    /// Heal, as the user defined it: remove every injury, Condition (= FMRTE's Match Exercise) and Match Sharpness to
    /// 10000, Jadedness to -500. Each step is guarded; a failure stops before the remaining steps.
    /// </summary>
    public void Heal(PlayerRecord player) => new PlayerHealer(_memory, Profile, Game.ModuleBase).Heal(player);

    /// <summary>Inspire, as the user defined it: Morale to 20.</summary>
    public void Inspire(PlayerRecord player) => new PlayerInspirer(_memory, Profile, Game.ModuleBase).Inspire(player);

    /// <summary>Applies the user's Destroy values through the guarded player, contract and cohesion writers.</summary>
    public void Destroy(PlayerRecord player)
    {
        new PlayerDestroyer(_memory, Profile, Game.ModuleBase).Destroy(player);
        if (Contracts.Read(player) is { Happiness: { } happiness } contract && happiness != 0)
            Contracts.SetHappiness(player, contract, 0);
        if (Contracts.Read(player) is { PlayingTimeHappiness: { } playing } refreshed && playing != 0)
            Contracts.SetPlayingTimeHappiness(player, refreshed, 0);
        if (PlayerFamiliarity.Read(player.Address) is { } familiarity && familiarity.Tactical != 0)
            PlayerFamiliarity.SetTactical(familiarity, 0);
        if (player.Club is not null) SetTeamCohesion(player, TeamCohesionWriter.Destroy);
    }

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
    public IReadOnlyList<Clubs.ClubVisionEntry> ReadClubVision(ulong teamAddress, IReadOnlyList<Clubs.ClubEntry>? clubs = null) =>
        new Clubs.ClubListReader(_memory, Profile, ModuleBase).ReadClubVision(teamAddress, clubs);
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
    public Clubs.ClubSponsorWriter SponsorWriter => new(_memory, Profile, ModuleBase, _memory.Allocate);
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
            try { memory = new WindowsProcessMemory(game.ProcessId, false); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or MemoryAccessException)
            { accessError = ex; continue; }
            try
            {
                foreach (var profile in all)
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
