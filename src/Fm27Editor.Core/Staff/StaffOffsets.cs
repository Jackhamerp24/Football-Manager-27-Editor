using System.Text.Json;

namespace Fm27Editor.Core.Staff;

/// <summary>
/// Non-player staff (profile <c>staff</c>), measured on FM27 27.0.0 against FMRTE 27 (Man Utd: Alex Ferguson, Kenny
/// Dalglish). The game keeps every person in one vector of pointers at [module + PersonListRootRva] + PersonListOffset;
/// a staff member's entry points at the <b>person part</b> of an object whose <b>staff part</b> (CA, PA, reputations,
/// attributes; the FM26 staff class layout) sits <see cref="StaffKind.StaffPart"/> bytes below it: 0x100 in the 0x240-byte
/// objects of ordinary staff, 0x440 in the 0x580-byte objects of human managers (Duong Vu, checked against FMRTE).
/// Person-part offsets: ID, names, nation, personality, birth date, job record. Staff-part offsets: the rest.
/// </summary>
public sealed class StaffOffsets
{
    public required uint PersonListRootRva { get; init; }
    public required int PersonListOffset { get; init; }
    /// <summary>The root is a begin/end vector of staff-part pointers rather than a person-list holder.</summary>
    public bool DirectStaffVector { get; init; }
    public required IReadOnlyList<StaffKind> Kinds { get; init; }
    /// <summary>Human-manager attributes (1–100, stored as is), staff-part offsets; only on kinds with Manager set.</summary>
    public required IReadOnlyDictionary<ManagerAttribute, int> ManagerAttributes { get; init; }

    public required int Id { get; init; }
    public required int FullName { get; init; }
    public required int FirstName { get; init; }
    public required int LastName { get; init; }
    public required int CommonName { get; init; }
    public required int Nation { get; init; }
    /// <summary>8 bytes, <see cref="StaffPersonality"/> order.</summary>
    public required int Personality { get; init; }
    public required int DateOfBirth { get; init; }
    /// <summary>Pointer to the job record: +JobPerson back to the person part, +JobTeam team, int16 +JobCode.</summary>
    public required int Job { get; init; }
    public required int JobPerson { get; init; }
    public required int JobTeam { get; init; }
    public required int JobCode { get; init; }

    public required int HomeReputation { get; init; }
    public required int CurrentReputation { get; init; }
    public required int WorldReputation { get; init; }
    public required int CurrentAbility { get; init; }
    public required int PotentialAbility { get; init; }
    public int? KnowledgeHolder { get; init; }
    public int? KnowledgeValue { get; init; }
    public int? TendenciesHolder { get; init; }
    public int? Formations { get; init; }
    public int? ContractRolling { get; init; }
    public int? ContractRollingMask { get; init; }
    /// <summary>Staff-part byte offset per attribute and whether it is stored ×5 (most) or as 1–20.</summary>
    public required IReadOnlyDictionary<StaffAttribute, (int Offset, bool Scaled)> Attributes { get; init; }

    static readonly string[] Keys =
    [
        "personListOffset", "id", "fullName", "firstName", "lastName", "commonName", "nation", "personality",
        "dateOfBirth", "job", "jobPerson", "jobTeam", "jobCode", "homeReputation", "currentReputation", "worldReputation",
        "currentAbility", "potentialAbility",
    ];

    static int Hex(JsonElement e, string key) => e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
        ? Convert.ToInt32(s[2..], 16) : throw new FormatException($"staff.{key}: expected hex offset");

    internal static StaffOffsets Load(JsonElement e)
    {
        var attrs = new Dictionary<StaffAttribute, (int, bool)>();
        foreach (var (key, scaled) in new[] { ("attributes", true), ("rawAttributes", false) })
            foreach (var p in e.GetProperty(key).EnumerateObject())
                attrs.Add(Enum.Parse<StaffAttribute>(p.Name), (Hex(e.GetProperty(key), p.Name), scaled));
        var missing = Enum.GetValues<StaffAttribute>().Where(a => !attrs.ContainsKey(a)).ToList();
        if (missing.Count > 0) throw new FormatException($"staff attributes missing: {string.Join(", ", missing)}");
        var kinds = e.GetProperty("kinds").EnumerateArray().Select(k => new StaffKind(
            (uint)Hex(k, "personVtable"), (uint)Hex(k, "staffVtable"), Hex(k, "staffPart"),
            k.TryGetProperty("manager", out var m) && m.GetBoolean())).ToList();
        var mgr = e.GetProperty("managerAttributes").EnumerateObject()
            .ToDictionary(p => Enum.Parse<ManagerAttribute>(p.Name), p => Hex(e.GetProperty("managerAttributes"), p.Name));
        return new StaffOffsets
        {
            PersonListRootRva = (uint)Hex(e, "personListRootRva"), PersonListOffset = Hex(e, "personListOffset"),
            DirectStaffVector = e.TryGetProperty("directStaffVector", out var direct) && direct.GetBoolean(),
            Kinds = kinds, ManagerAttributes = mgr,
            Id = Hex(e, "id"), FullName = Hex(e, "fullName"), FirstName = Hex(e, "firstName"), LastName = Hex(e, "lastName"),
            CommonName = Hex(e, "commonName"), Nation = Hex(e, "nation"), Personality = Hex(e, "personality"),
            DateOfBirth = Hex(e, "dateOfBirth"), Job = Hex(e, "job"), JobPerson = Hex(e, "jobPerson"), JobTeam = Hex(e, "jobTeam"),
            JobCode = Hex(e, "jobCode"), HomeReputation = Hex(e, "homeReputation"), CurrentReputation = Hex(e, "currentReputation"),
            WorldReputation = Hex(e, "worldReputation"), CurrentAbility = Hex(e, "currentAbility"), PotentialAbility = Hex(e, "potentialAbility"),
            Attributes = attrs,
            KnowledgeHolder = e.TryGetProperty("knowledgeHolder", out _) ? Hex(e, "knowledgeHolder") : null,
            KnowledgeValue = e.TryGetProperty("knowledgeValue", out _) ? Hex(e, "knowledgeValue") : null,
            TendenciesHolder = e.TryGetProperty("tendenciesHolder", out _) ? Hex(e, "tendenciesHolder") : null,
            Formations = e.TryGetProperty("formations", out _) ? Hex(e, "formations") : null,
            ContractRolling = e.TryGetProperty("contractRolling", out _) ? Hex(e, "contractRolling") : null,
            ContractRollingMask = e.TryGetProperty("contractRollingMask", out _) ? Hex(e, "contractRollingMask") : null,
        };
    }

    internal void Write(Utf8JsonWriter w)
    {
        void H(string k, long v) => w.WriteString(k, $"0x{v:X}");
        w.WriteStartObject("staff");
        if (KnowledgeHolder is { } kh) H("knowledgeHolder", kh);
        if (KnowledgeValue is { } kv) H("knowledgeValue", kv);
        if (TendenciesHolder is { } th) H("tendenciesHolder", th);
        if (Formations is { } fo) H("formations", fo);
        if (ContractRolling is { } cr) H("contractRolling", cr);
        if (ContractRollingMask is { } crm) H("contractRollingMask", crm);
        H("personListRootRva", PersonListRootRva); H("personListOffset", PersonListOffset);
        if (DirectStaffVector) w.WriteBoolean("directStaffVector", true);
        w.WriteStartArray("kinds");
        foreach (var k in Kinds)
        {
            w.WriteStartObject();
            H("personVtable", k.PersonVtableRva); H("staffVtable", k.StaffVtableRva); H("staffPart", k.StaffPart);
            if (k.Manager) w.WriteBoolean("manager", true);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        int[] v = [PersonListOffset, Id, FullName, FirstName, LastName, CommonName, Nation, Personality, DateOfBirth, Job,
            JobPerson, JobTeam, JobCode, HomeReputation, CurrentReputation, WorldReputation, CurrentAbility, PotentialAbility];
        for (int i = 1; i < Keys.Length; i++) H(Keys[i], v[i]);
        w.WriteStartObject("managerAttributes");
        foreach (var (a, o) in ManagerAttributes.OrderBy(kv => kv.Key)) H(a.ToString(), o);
        w.WriteEndObject();
        foreach (var (key, scaled) in new[] { ("attributes", true), ("rawAttributes", false) })
        {
            w.WriteStartObject(key);
            foreach (var (a, o) in Attributes.Where(kv => kv.Value.Scaled == scaled).OrderBy(kv => kv.Key)) H(a.ToString(), o.Offset);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }
}

/// <summary>Staff attributes as FMRTE 27 groups them (Coaching, Tactical, Non Tactical, Mental, Scouting, Chairman).</summary>
public enum StaffAttribute
{
    CoachingAttacking, CoachingDefending, CoachingFitness, CoachingGoalkeeping, CoachingPossession, CoachingPlayer,
    CoachingTactical, CoachingTechnical, PeopleManagement, WorkingWithYoungsters, DirtinessAllowance, Versatility, SetPieces,
    TacticalAttacking, Depth, Directness, Flamboyancy, Flexibility, FreeRoles, Marking, Offside, Pressing, SittingBack, Tempo,
    UseOfPlaymaker, UseOfSubstitutions, Width,
    BuyingPlayers, HardnessOfTraining, MindGames, SquadRotation,
    Determination, JudgingPlayerAbility, JudgingPlayerPotential, JudgingStaffAbility, Negotiating, Authority, Motivating,
    TacticalKnowledge, Physiotherapy, SportsScience,
    JudgingPlayerData, JudgingTeamData, PresentingData,
    Business, Interference, Patience, Resources,
}

/// <summary>One staff object class: vtables of its person and staff parts, and how far below the person part the staff part is.</summary>
public sealed record StaffKind(uint PersonVtableRva, uint StaffVtableRva, int StaffPart, bool Manager);

/// <summary>Human-manager attributes, FMRTE 27 order. Staff-part bytes run in reverse (Media Handling last).</summary>
public enum ManagerAttribute
{
    HandsOnApproach, DomesticPlayerBias, TacticalConsistency, ClubPlayerLoyalty, SquadDiscipline, FinancialControl, MediaHandling,
}

public enum StaffPersonality { Adaptability, Ambition, Loyalty, Pressure, Professionalism, Sportsmanship, Temperament, Controversy }
