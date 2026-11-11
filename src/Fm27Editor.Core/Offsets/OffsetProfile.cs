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
    /// <summary>Actual game build when Build also identifies a storefront-specific profile.</summary>
    public string? MatchBuild { get; init; }
    /// <summary>Installed flavour this profile belongs to ("gamepass" for the Microsoft Store image), or null.</summary>
    public string? Variant { get; init; }
    public uint? ImageSize { get; init; }
    public uint? PeTimestamp { get; init; }
    /// <summary>Experimental layouts must never open the game with write access.</summary>
    public bool ReadOnly { get; init; }
    /// <summary>Individually verified attributes writable even when the rest of the profile is read-only.</summary>

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
    public AwardOffsets? Award { get; init; }
    public GameDateOffsets? GameDate { get; init; }
    /// <summary>Address of a packed in-game date stored directly in the module.</summary>
    public uint? DirectGameDateRva { get; init; }
    /// <summary>The planned-retirement manager: the module slot holding it and its vtable (null when the build was not measured).</summary>
    public uint? RetirementManagerRva { get; init; }
    public uint? RetirementManagerVtable { get; init; }
    /// <summary>The contract object kind used for a national appointment (a staff member's separate job with a national team).</summary>
    public uint? NationAppointmentVtable { get; init; }
    /// <summary>Classes of the targets of nation agreement rows: a REGION and an AGREEMENT object.</summary>
    public uint? AgreementRegionVtable { get; init; }
    public uint? AgreementVtable { get; init; }
    /// <summary>Profile <c>forceAccept</c>: RVAs of the code sites the force-accept presets patch, null when this build was not measured.</summary>
    public ForceAccept.ForceAcceptOffsets? ForceAccept { get; init; }
    /// <summary>Player values beyond attributes (profile <c>player.fields</c>, paths from the player object).</summary>
    public IReadOnlyDictionary<Players.PlayerField, Clubs.ClubFieldOffset>? PlayerFields { get; init; }
    public InjuryOffsets? Injuries { get; init; }
    public RelationOffsets? Relations { get; init; }
    public BanOffsets? Bans { get; init; }
    public LanguageOffsets? Languages { get; init; }
    public PromiseOffsets? Promises { get; init; }
    public TacticalUnderstandingOffsets? TacticalUnderstanding { get; init; }
    public CityOffsets? Cities { get; init; }
    public CohesionOffsets? Cohesion { get; init; }
    public CareerPlanOffsets? CareerPlans { get; init; }
    public ProposalOffsets? Proposals { get; init; }
    public FutureTransferOffsets? FutureTransfers { get; init; }
    public CallUpOffsets? CallUps { get; init; }
    /// <summary>Player offset of the 8-byte preferred-moves bit field (profile <c>player.preferredMoves</c>).</summary>
    public int? PreferredMoves { get; init; }
    /// <summary>Profile <c>stadium</c>.</summary>
    public Stadiums.StadiumOffsets? Stadium { get; init; }
    /// <summary>Profile <c>staff</c>.</summary>
    public Staff.StaffOffsets? Staff { get; init; }
    /// <summary>Profile <c>contract.unhappiness</c>.</summary>
    public UnhappinessOffsets? Unhappiness { get; init; }

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
            MatchBuild = root.TryGetProperty("matchBuild", out var mb) ? mb.GetString() : null,
        Variant = root.TryGetProperty("variant", out var va) ? va.GetString() : null,
            ImageSize = root.TryGetProperty("imageSize", out _) ? (uint)Hex(root, "imageSize") : null,
            PeTimestamp = root.TryGetProperty("peTimestamp", out _) ? (uint)Hex(root, "peTimestamp") : null,
            ReadOnly = root.TryGetProperty("readOnly", out var ro) && ro.GetBoolean(),
            DirectGameDateRva = root.TryGetProperty("directGameDateRva", out _) ? (uint)Hex(root, "directGameDateRva") : null,
            RetirementManagerRva = root.TryGetProperty("retirementManagerRva", out _) ? (uint)Hex(root, "retirementManagerRva") : null,
            RetirementManagerVtable = root.TryGetProperty("retirementManagerVtable", out _) ? (uint)Hex(root, "retirementManagerVtable") : null,
            NationAppointmentVtable = root.TryGetProperty("nationAppointmentVtable", out _) ? (uint)Hex(root, "nationAppointmentVtable") : null,
            AgreementRegionVtable = root.TryGetProperty("agreementRegionVtable", out _) ? (uint)Hex(root, "agreementRegionVtable") : null,
            AgreementVtable = root.TryGetProperty("agreementVtable", out _) ? (uint)Hex(root, "agreementVtable") : null,
            ForceAccept = root.TryGetProperty("forceAccept", out var fa) ? new ForceAccept.ForceAcceptOffsets
            {
                BoardRequestsRva = fa.TryGetProperty("boardRequestsRva", out _) ? (uint)Hex(fa, "boardRequestsRva") : null,
                ClubVisionRva = fa.TryGetProperty("clubVisionRva", out _) ? (uint)Hex(fa, "clubVisionRva") : null,
                ContractOffersRva = fa.TryGetProperty("contractOffersRva", out _) ? (uint)Hex(fa, "contractOffersRva") : null,
                TransferOffersRva = fa.TryGetProperty("transferOffersRva", out _) ? (uint)Hex(fa, "transferOffersRva") : null,
            } : null,
            PlayerListRootRva = (uint)Hex(list, "rootRva"),
            PlayerListBeginOffset = Hex(list, "beginOffset"),
            PlayerListEndOffset = Hex(list, "endOffset"),
            PlayerVtableRvas = root.GetProperty("playerVtables").EnumerateArray().Select(v => (uint)ParseHex(v.GetString())).ToArray(),
            PlayerObjectSize = Hex(player, "objectSize"),
            AttributeScale = player.GetProperty("attributeScale").GetInt32(),
            Attributes = EnumMap<PlayerAttribute>(player.GetProperty("attributes")),
            Positions = EnumMap<PlayerPosition>(player.GetProperty("positions")),
            Personality = EnumMap<PersonalityAttribute>(player.GetProperty("personality")),
            PlayerFields = player.TryGetProperty("fields", out var pf) ? FieldMap<PlayerField>(pf, rootRequired: false) : null,
            Injuries = player.TryGetProperty("injuries", out var inj) ? InjuryOffsets.Load(inj) : null,
            Relations = player.TryGetProperty("relations", out var rel) ? RelationOffsets.Load(rel) : null,
            Bans = player.TryGetProperty("bans", out var bans) ? BanOffsets.Load(bans) : null,
            Languages = player.TryGetProperty("languages", out var lang) ? LanguageOffsets.Load(lang) : null,
            Promises = player.TryGetProperty("promises", out var prom) ? PromiseOffsets.Load(prom) : null,
            TacticalUnderstanding = player.TryGetProperty("tacticalUnderstanding", out var tu) ? TacticalUnderstandingOffsets.Load(tu) : null,
            Cities = player.TryGetProperty("cities", out var city) ? CityOffsets.Load(city) : null,
            Cohesion = player.TryGetProperty("cohesion", out var coh) ? CohesionOffsets.Load(coh) : null,
            CareerPlans = player.TryGetProperty("careerPlans", out var cp) ? CareerPlanOffsets.Load(cp) : null,
            Proposals = player.TryGetProperty("proposals", out var tp) ? ProposalOffsets.Load(tp) : null,
            FutureTransfers = player.TryGetProperty("futureTransfers", out var ft) ? FutureTransferOffsets.Load(ft) : null,
            CallUps = player.TryGetProperty("callUps", out var cu) ? CallUpOffsets.Load(cu) : null,
            PreferredMoves = player.TryGetProperty("preferredMoves", out _) ? Hex(player, "preferredMoves") : null,
            Stadium = root.TryGetProperty("stadium", out var sd)
                ? Fm27Editor.Core.Stadiums.StadiumOffsets.Load(sd, e => FieldMap<Stadiums.StadiumField>(e, rootRequired: false)) : null,
            Staff = root.TryGetProperty("staff", out var sf) ? Fm27Editor.Core.Staff.StaffOffsets.Load(sf) : null,
            Unhappiness = root.TryGetProperty("contract", out var uc) && uc.TryGetProperty("unhappiness", out var un)
                ? UnhappinessOffsets.Load(un) : null,
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
                SeniorTeam = OptHex(c, "seniorTeam"),
                Owner = OptHex(c, "owner"),
                Job = OptHex(c, "job"),
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
                PlayerJoinedClub = OptHex(c, "playerJoinedClub"),
                Clauses = OptHex(c, "clauses"),
            } : null,
            Team = root.TryGetProperty("team", out var tm) ? new TeamOffsets
            {
                VtableRva = (uint)Hex(tm, "vtable"),
                PlayerTeam = Hex(tm, "playerTeam"),
                Squad = Hex(tm, "squad"),
                Classification = OptHex(tm, "classification"),
                AgeLimit = OptHex(tm, "ageLimit"),
                Captain = OptHex(tm, "captain"),
                ViceCaptain = OptHex(tm, "viceCaptain"),
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
                Relationships = k.TryGetProperty("relationships", out var clubRel) ? ClubRelationshipOffsets.Load(clubRel) : null,
                ClubVtableRva = (uint)Hex(k, "vtable"),
                Name = Hex(k, "name"),
                ShortName = Hex(k, "shortName"),
                Id = OptHex(k, "id"),
                Nation = OptHex(k, "nation"),
                MedicalStaff = OptHex(k, "medicalStaff"), CoachingStaff = OptHex(k, "coachingStaff"),
                ScoutingStaff = OptHex(k, "scoutingStaff"), Gender = OptHex(k, "gender"),
                StaffAllocationClasses = k.TryGetProperty("staffAllocationClasses", out var classes)
                    ? classes.EnumerateArray().Select(e => e.GetInt32()).ToArray() : null,
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
                    Scan = tc.TryGetProperty("scan", out var scan) && scan.GetBoolean(),
                    SellOnVtableRva = (uint)Hex(tc, "sellOnVtable"),
                    AppearanceFeeVtableRva = (uint?)OptHex(tc, "appearanceFeeVtable"),
                    CompetitionBonusVtableRva = (uint?)OptHex(tc, "competitionBonusVtable"),
                } : null,
                Fields = k.TryGetProperty("fields", out var cf) ? FieldMap<Clubs.ClubField>(cf, rootRequired: true) : null,
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
                ListRootRva = (uint?)OptHex(nat, "listRootRva"),
                ListOffset = OptHex(nat, "listOffset"),
                Id = OptHex(nat, "id"),
                Capital = OptHex(nat, "capital"),
                Continent = OptHex(nat, "continent"),
                Stadium = OptHex(nat, "stadium"),
                Teams = nat.TryGetProperty("teams", out var nt) ? new NationTeamOffsets(Hex(nt, "pointer"), Hex(nt, "list"), (uint)Hex(nt, "teamVtable"), Hex(nt, "reputation")) : null,
                WomenTeam = OptHex(nat, "womenTeam"),
                ContainerVtable = nat.TryGetProperty("containerVtable", out _) ? (uint?)Hex(nat, "containerVtable") : null,
                ContainerNation = OptHex(nat, "containerNation"),
                Rivalries = nat.TryGetProperty("rivalries", out var nr) ? new NationRivalryOffsets(
                    Hex(nr, "team"), (uint)Hex(nr, "teamVtable"), Hex(nr, "vector"), Hex(nr, "entrySize"),
                    Hex(nr, "type"), Hex(nr, "rivalType"), Hex(nr, "value")) : null,
                Preferences = nat.TryGetProperty("preferences", out var np) ? new NationPreferenceOffsets(
                    Hex(np, "team"), (uint)Hex(np, "teamVtable"), Hex(np, "vector"), Hex(np, "entrySize")) : null,
                Fields = nat.TryGetProperty("fields", out var nf) ? FieldMap<Nations.NationField>(nf, rootRequired: false) : null,
            } : null,
            Competition = root.TryGetProperty("competition", out var comp) ? new CompetitionOffsets
            {
                TeamCompetition = Hex(comp, "teamCompetition"),
                VtableRva = (uint)Hex(comp, "vtable"),
                Name = Hex(comp, "name"),
                ShortName = Hex(comp, "shortName"),
                Index = OptHex(comp, "index"),
                Id = OptHex(comp, "id"),
                ListRootRva = OptHex(comp, "listRootRva"),
                ListOffset = OptHex(comp, "listOffset"),
                Reputation = OptHex(comp, "reputation"),
                ReputationMirror = OptHex(comp, "reputationMirror"),
                Foreground = OptHex(comp, "foreground"),
                Background = OptHex(comp, "background"),
                Trim = OptHex(comp, "trim"),
                WinterBallColour = OptHex(comp, "winterBallColour"),
                WinterBallStart = OptHex(comp, "winterBallStart"),
                WinterBallEnd = OptHex(comp, "winterBallEnd"),
                SubstitutionRules = OptHex(comp, "substitutionRules"),
                ThreeLetterName = OptHex(comp, "threeLetterName"),
                Type = OptHex(comp, "type"),
                UsesExtraOfficials = OptHex(comp, "usesExtraOfficials"),
                SeatedStadiumsOnly = OptHex(comp, "seatedStadiumsOnly"),
                LastContinentalPosition = OptHex(comp, "lastContinentalPosition"),
                ContinentalPosition = OptHex(comp, "continentalPosition"),
                CompetitionLevel = OptHex(comp, "competitionLevel"),
                Trophy = OptHex(comp, "trophy"),
                LowestOfficialCategory = OptHex(comp, "lowestOfficialCategory"),
                Flags = OptHex(comp, "flags"),
                SquadRulesContainerVtableRva = (uint?)OptHex(comp, "squadRulesContainerVtable"),
                PrizeMoney = comp.TryGetProperty("prizeMoney", out var prizeOffsets) ? new CompetitionPrizeOffsets(
                    Hex(prizeOffsets, "holder"), Hex(prizeOffsets, "stages"), Hex(prizeOffsets, "stageOwner"), Hex(prizeOffsets, "stagePrize"),
                    Hex(prizeOffsets, "perWin"), Hex(prizeOffsets, "perDraw"), Hex(prizeOffsets, "perDefeat"), Hex(prizeOffsets, "classification"),
                    (uint)Hex(prizeOffsets, "holderVtable"), (uint)Hex(prizeOffsets, "stageVtable"), (uint)Hex(prizeOffsets, "prizeVtable")) : null,
            } : null,
            Award = root.TryGetProperty("award", out var aw) ? new AwardOffsets
            {
                VtableRva = (uint)Hex(aw, "vtable"),
                ListRootRva = Hex(aw, "listRootRva"),
                ListOffset = Hex(aw, "listOffset"),
                Index = Hex(aw, "index"),
                Id = Hex(aw, "id"),
                Name = Hex(aw, "name"),
                ShortName = Hex(aw, "shortName"),
                Reputation = Hex(aw, "reputation"),
                WinnerHomeReputation = Hex(aw, "winnerHomeReputation"),
                WinnerWorldReputation = Hex(aw, "winnerWorldReputation"),
            } : null,
        };
    }

    /// <summary>Field maps share one shape; player fields have no root (the player object), club fields name club or team.</summary>
    static IReadOnlyDictionary<TField, Clubs.ClubFieldOffset> FieldMap<TField>(JsonElement obj, bool rootRequired) where TField : struct, Enum
    {
        var map = new Dictionary<TField, Clubs.ClubFieldOffset>();
        foreach (var p in obj.EnumerateObject())
        {
            if (!Enum.TryParse<TField>(p.Name, out var field)) throw new FormatException($"Unknown {typeof(TField).Name} '{p.Name}'");
            var v = p.Value;
            string Str(string name) => v.TryGetProperty(name, out var s) ? s.GetString() ?? "" : throw new FormatException($"{p.Name}: {name} is missing");
            map[field] = new Clubs.ClubFieldOffset(
                rootRequired || v.TryGetProperty("root", out _) ? Enum.Parse<Clubs.ClubFieldRoot>(Str("root"), ignoreCase: true) : Clubs.ClubFieldRoot.Club,
                Clubs.ClubFieldOffset.ParsePath(Str("path")),
                Enum.Parse<Clubs.ClubFieldType>(Str("type"), ignoreCase: true),
                v.TryGetProperty("scale", out var sc) ? sc.GetInt32() : 1,
                v.TryGetProperty("objectVtables", out var ov) ? ov.EnumerateArray().Select(x => (uint)ParseHex(x.GetString())).ToArray() : null,
                v.TryGetProperty("mask", out var mk) ? (int)ParseHex(mk.GetString()) : 0);
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
            if (MatchBuild is { } matchBuild) w.WriteString("matchBuild", matchBuild);
        if (Variant is { } variant) w.WriteString("variant", variant);
            if (ImageSize is { } imageSize) H("imageSize", imageSize);
            if (PeTimestamp is { } timestamp) H("peTimestamp", timestamp);
            if (ReadOnly) w.WriteBoolean("readOnly", true);
            if (DirectGameDateRva is { } directDate) H("directGameDateRva", directDate);
            if (RetirementManagerRva is { } retirementRva) H("retirementManagerRva", retirementRva);
            if (RetirementManagerVtable is { } retirementVtable) H("retirementManagerVtable", retirementVtable);
            if (NationAppointmentVtable is { } appointmentVtable) H("nationAppointmentVtable", appointmentVtable);
            if (AgreementRegionVtable is { } agreementRegion) H("agreementRegionVtable", agreementRegion);
            if (AgreementVtable is { } agreementKind) H("agreementVtable", agreementKind);
            if (ForceAccept is { } forceAccept)
            {
                w.WriteStartObject("forceAccept");
                if (forceAccept.BoardRequestsRva is { } boardRequests) H("boardRequestsRva", boardRequests);
                if (forceAccept.ClubVisionRva is { } clubVision) H("clubVisionRva", clubVision);
                if (forceAccept.ContractOffersRva is { } contractOffers) H("contractOffersRva", contractOffers);
                if (forceAccept.TransferOffersRva is { } transferOffers) H("transferOffersRva", transferOffers);
                w.WriteEndObject();
            }
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
            Injuries?.Write(w);
            Relations?.Write(w);
            Bans?.Write(w);
            Languages?.Write(w);
            Promises?.Write(w);
            TacticalUnderstanding?.Write(w);
            Cities?.Write(w);
            Cohesion?.Write(w);
            CareerPlans?.Write(w);
            Proposals?.Write(w);
            FutureTransfers?.Write(w);
            CallUps?.Write(w);
            if (PreferredMoves is { } pm) H("preferredMoves", pm);
            if (PlayerFields is { Count: > 0 } pfs)
            {
                w.WriteStartObject("fields");
                foreach (var (field, o) in pfs.OrderBy(kv => kv.Key))
                {
                    w.WriteStartObject(field.ToString());
                    w.WriteString("path", o.PathText);
                    w.WriteString("type", o.Type.ToString().ToLowerInvariant());
                    if (o.Scale != 1) w.WriteNumber("scale", o.Scale);
                    if (o.Mask != 0) w.WriteString("mask", $"0x{o.Mask:X}");
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
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
                O("seniorTeam", c.SeniorTeam);
                O("owner", c.Owner); O("job", c.Job);
                O("playingTime", c.PlayingTime); O("type", c.Type); O("started", c.Started); O("loyaltyBonus", c.LoyaltyBonus);
                O("squadNumber", c.SquadNumber); O("happiness", c.Happiness); O("playingTimeHappiness", c.PlayingTimeHappiness);
                O("futurePlayingTime", c.FuturePlayingTime); O("futurePlayingTimeYear", c.FuturePlayingTimeYear);
                O("playerJoinedClub", c.PlayerJoinedClub); O("clauses", c.Clauses);
                Unhappiness?.Write(w);
                w.WriteEndObject();
            }
            Staff?.Write(w);
            Stadium?.Write(w);
            if (Team is { } tm)
            {
                w.WriteStartObject("team");
                H("vtable", tm.VtableRva); H("playerTeam", tm.PlayerTeam); H("squad", tm.Squad);
                if (tm.Classification is { } tc) H("classification", tc);
                if (tm.AgeLimit is { } ta) H("ageLimit", ta);
                if (tm.Captain is { } captain) H("captain", captain);
                if (tm.ViceCaptain is { } viceCaptain) H("viceCaptain", viceCaptain);
                w.WriteEndObject();
            }
            if (Club is { } k)
            {
                w.WriteStartObject("club");
                k.Lists?.Write(w);
                k.FinanceLists?.Write(w);
                k.Relationships?.Write(w);
                H("vtable", k.ClubVtableRva); H("name", k.Name); H("shortName", k.ShortName);
                if (k.Id is { } id) H("id", id);
                if (k.Nation is { } cn) H("nation", cn);
                if (k.MedicalStaff is { } ms) H("medicalStaff", ms);
                if (k.CoachingStaff is { } coaching) H("coachingStaff", coaching);
                if (k.ScoutingStaff is { } ss) H("scoutingStaff", ss);
                if (k.Gender is { } gender) H("gender", gender);
                if (k.StaffAllocationClasses is { } sizes)
                {
                    w.WriteStartArray("staffAllocationClasses");
                    foreach (int size in sizes) w.WriteNumberValue(size);
                    w.WriteEndArray();
                }
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
                        if (o.Mask != 0) w.WriteString("mask", $"0x{o.Mask:X}");
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
                if (nat.ListRootRva is { } nlr) H("listRootRva", nlr);
                if (nat.ListOffset is { } nlo) H("listOffset", nlo);
                if (nat.Id is { } nid) H("id", nid);
                if (nat.Capital is { } ncap) H("capital", ncap);
                if (nat.Continent is { } ncon) H("continent", ncon);
                if (nat.Stadium is { } nstd) H("stadium", nstd);
                if (nat.Teams is { } nts)
                {
                    w.WriteStartObject("teams");
                    H("pointer", nts.Pointer); H("list", nts.List); H("teamVtable", nts.TeamVtable); H("reputation", nts.Reputation);
                    w.WriteEndObject();
                }
                if (nat.WomenTeam is { } women) H("womenTeam", women);
                if (nat.ContainerVtable is { } ncv) H("containerVtable", ncv);
                if (nat.ContainerNation is { } ncn) H("containerNation", ncn);
                if (nat.Rivalries is { } nr)
                {
                    w.WriteStartObject("rivalries");
                    H("team", nr.Team); H("teamVtable", nr.TeamVtable); H("vector", nr.Vector);
                    H("entrySize", nr.EntrySize); H("type", nr.Type); H("rivalType", nr.RivalType); H("value", nr.Value);
                    w.WriteEndObject();
                }
                if (nat.Preferences is { } np)
                {
                    w.WriteStartObject("preferences");
                    H("team", np.Team); H("teamVtable", np.TeamVtable); H("vector", np.Vector); H("entrySize", np.EntrySize);
                    w.WriteEndObject();
                }
                if (nat.Fields is { Count: > 0 } nfs)
                {
                    w.WriteStartObject("fields");
                    foreach (var (field, o) in nfs.OrderBy(kv => kv.Key))
                    {
                        w.WriteStartObject(field.ToString());
                        w.WriteString("path", o.PathText);
                        w.WriteString("type", o.Type.ToString().ToLowerInvariant());
                        if (o.Mask != 0) w.WriteString("mask", $"0x{o.Mask:X}");
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            if (Competition is { } comp)
            {
                w.WriteStartObject("competition");
                H("teamCompetition", comp.TeamCompetition); H("vtable", comp.VtableRva); H("name", comp.Name); H("shortName", comp.ShortName);
                if (comp.Index is { } ci) H("index", ci);
                if (comp.Id is { } cid) H("id", cid);
                foreach (var (key, value) in new (string, int?)[]
                {
                    ("listRootRva", comp.ListRootRva), ("listOffset", comp.ListOffset), ("reputation", comp.Reputation),
                    ("reputationMirror", comp.ReputationMirror), ("foreground", comp.Foreground), ("background", comp.Background),
                    ("trim", comp.Trim), ("winterBallColour", comp.WinterBallColour), ("winterBallStart", comp.WinterBallStart),
                    ("winterBallEnd", comp.WinterBallEnd), ("substitutionRules", comp.SubstitutionRules),
                    ("threeLetterName", comp.ThreeLetterName), ("type", comp.Type),
                    ("usesExtraOfficials", comp.UsesExtraOfficials), ("seatedStadiumsOnly", comp.SeatedStadiumsOnly),
                    ("lastContinentalPosition", comp.LastContinentalPosition), ("continentalPosition", comp.ContinentalPosition),
                    ("competitionLevel", comp.CompetitionLevel), ("trophy", comp.Trophy),
                    ("lowestOfficialCategory", comp.LowestOfficialCategory), ("flags", comp.Flags),
                })
                    if (value is { } v) H(key, v);
                if (comp.PrizeMoney is { } prize)
                {
                    w.WriteStartObject("prizeMoney");
                    H("holder", prize.Holder); H("stages", prize.Stages); H("stageOwner", prize.StageOwner); H("stagePrize", prize.StagePrize);
                    H("perWin", prize.PerWin); H("perDraw", prize.PerDraw); H("perDefeat", prize.PerDefeat); H("classification", prize.Classification);
                    H("holderVtable", prize.HolderVtableRva); H("stageVtable", prize.StageVtableRva); H("prizeVtable", prize.PrizeVtableRva);
                    w.WriteEndObject();
                }
                if (comp.SquadRulesContainerVtableRva is { } squadRulesVtable) H("squadRulesContainerVtable", squadRulesVtable);
                w.WriteEndObject();
            }
            if (Award is { } aw)
            {
                w.WriteStartObject("award");
                H("vtable", aw.VtableRva); H("listRootRva", aw.ListRootRva); H("listOffset", aw.ListOffset); H("index", aw.Index);
                H("id", aw.Id); H("name", aw.Name); H("shortName", aw.ShortName); H("reputation", aw.Reputation);
                H("winnerHomeReputation", aw.WinnerHomeReputation); H("winnerWorldReputation", aw.WinnerWorldReputation);
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
