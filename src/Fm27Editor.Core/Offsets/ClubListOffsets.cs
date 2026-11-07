using System.Text.Json;

namespace Fm27Editor.Core.Offsets;

/// <summary>Only list layouts with independent FMRTE oracles on Sheffield and Everton. All values are build-specific.</summary>
public sealed record ClubListOffsets(
    int Teams, int TeamDetails, int Vision, int VisionVector, uint VisionHolderVtable, uint CultureVtable,
    int CultureSize, int VisionType, int Importance, int VisionStart, int VisionEnd, int ItemIndex, int ItemMarker,
    int Affiliations, int AffiliationSize, int Parent, int Feeder, int AffiliationStart, int AffiliationType,
    int RegionalPointer, int RegionalSize, int Level)
{
    public bool VisionImportanceEditable { get; init; }
    /// <summary>Board-objective target (int32) and status (byte); -1 when the profile has no measured offsets.</summary>
    public int VisionTarget { get; init; } = -1;
    public int VisionStatus { get; init; } = -1;
    /// <summary>
    /// Extra class vtables seen for this build, space separated hex. Club culture records and the vision holder use
    /// different classes from national ones, so the reader accepts any listed value; empty means "just the field above".
    /// </summary>
    public string CultureVtables { get; init; } = "";
    public string VisionHolderVtables { get; init; } = "";

    public bool AcceptsCultureVtable(uint rva) => Accepts(CultureVtables, CultureVtable, rva);
    public bool AcceptsHolderVtable(uint rva) => Accepts(VisionHolderVtables, VisionHolderVtable, rva);

    static bool Accepts(string list, uint fallback, uint rva) => list.Length == 0
        ? rva == fallback
        : list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(v => Convert.ToUInt32(v, 16) == rva);
    public int? AffiliationAnnualFee { get; init; }
    public int? AffiliationEnd { get; init; }
    public bool AffiliationStartEditable { get; init; }
    public int? AffiliationFriendlyChance { get; init; }
    public int? AffiliationParentWagePercent { get; init; }
    public int? AffiliationPlayFriendlies { get; init; }
    public int? AffiliationFirstOption { get; init; }
    public int? AffiliationPlayersOnLoan { get; init; }
    public int? AffiliationScoutingShared { get; init; }
    internal static ClubListOffsets Load(JsonElement e)
    {
        int H(string key) => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"club.lists.{key}: expected hex offset"), 16);
        uint[] HexArray(string key, uint fallback) => e.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(v => (uint)Convert.ToInt32(v.GetString() is { } txt && txt.StartsWith("0x") ? txt[2..]
                : throw new FormatException($"club.lists.{key}: expected hex"), 16)).ToArray()
            : [fallback];
        var vhVtables = e.TryGetProperty("visionHolderVtables", out _)
            ? string.Join(' ', HexArray("visionHolderVtables", (uint)H("visionHolderVtable")).Select(v => v.ToString("X")))
            : "";
        var visionTarget = e.TryGetProperty("visionTarget", out _) ? H("visionTarget") : -1;
        var visionStatus = e.TryGetProperty("visionStatus", out _) ? H("visionStatus") : -1;
        var cultureVtables = e.TryGetProperty("cultureVtables", out _)
            ? string.Join(' ', HexArray("cultureVtables", (uint)H("cultureVtable")).Select(v => v.ToString("X")))
            : "";
        return new ClubListOffsets(H("teams"), H("teamDetails"), H("vision"), H("visionVector"), (uint)H("visionHolderVtable"),
            (uint)H("cultureVtable"), H("cultureSize"), H("visionType"), H("importance"), H("visionStart"),
            H("visionEnd"), H("itemIndex"), H("itemMarker"), H("affiliations"), H("affiliationSize"),
            H("parent"), H("feeder"), H("affiliationStart"), H("affiliationType"), H("regionalPointer"), H("regionalSize"), H("level"))
        { VisionHolderVtables = vhVtables, CultureVtables = cultureVtables,
          VisionTarget = visionTarget, VisionStatus = visionStatus,
          VisionImportanceEditable = e.TryGetProperty("visionImportanceEditable", out var visionEditable) && visionEditable.GetBoolean(),
          AffiliationAnnualFee = e.TryGetProperty("affiliationAnnualFee", out _) ? H("affiliationAnnualFee") : null,
          AffiliationEnd = e.TryGetProperty("affiliationEnd", out _) ? H("affiliationEnd") : null,
          AffiliationStartEditable = e.TryGetProperty("affiliationStartEditable", out var editable) && editable.GetBoolean(),
          AffiliationFriendlyChance = e.TryGetProperty("affiliationFriendlyChance", out _) ? H("affiliationFriendlyChance") : null,
          AffiliationParentWagePercent = e.TryGetProperty("affiliationParentWagePercent", out _) ? H("affiliationParentWagePercent") : null,
          AffiliationPlayFriendlies = e.TryGetProperty("affiliationPlayFriendlies", out _) ? H("affiliationPlayFriendlies") : null,
          AffiliationFirstOption = e.TryGetProperty("affiliationFirstOption", out _) ? H("affiliationFirstOption") : null,
          AffiliationPlayersOnLoan = e.TryGetProperty("affiliationPlayersOnLoan", out _) ? H("affiliationPlayersOnLoan") : null,
          AffiliationScoutingShared = e.TryGetProperty("affiliationScoutingShared", out _) ? H("affiliationScoutingShared") : null };
    }

    internal void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject("lists");
        (string Key, long Value)[] entries =
        [
            ("teams", Teams), ("teamDetails", TeamDetails), ("vision", Vision), ("visionVector", VisionVector),
            ("visionHolderVtable", VisionHolderVtable), ("cultureVtable", CultureVtable), ("cultureSize", CultureSize),
            ("visionType", VisionType), ("importance", Importance), ("visionStart", VisionStart), ("visionEnd", VisionEnd),
            ("itemIndex", ItemIndex), ("itemMarker", ItemMarker), ("affiliations", Affiliations),
            ("affiliationSize", AffiliationSize), ("parent", Parent), ("feeder", Feeder),
            ("affiliationStart", AffiliationStart), ("affiliationType", AffiliationType),
            ("regionalPointer", RegionalPointer), ("regionalSize", RegionalSize), ("level", Level),
        ];
        foreach (var (key, value) in entries) w.WriteString(key, $"0x{value:X}");
        if (VisionImportanceEditable) w.WriteBoolean("visionImportanceEditable", true);
        if (VisionTarget >= 0) w.WriteString("visionTarget", $"0x{VisionTarget:X}");
        if (VisionStatus >= 0) w.WriteString("visionStatus", $"0x{VisionStatus:X}");
        void WriteVtables(string key, string values)
        {
            if (values.Length == 0) return;
            w.WriteStartArray(key);
            foreach (var v in values.Split(' ', StringSplitOptions.RemoveEmptyEntries)) w.WriteStringValue($"0x{Convert.ToUInt32(v, 16):X}");
            w.WriteEndArray();
        }
        WriteVtables("visionHolderVtables", VisionHolderVtables);
        WriteVtables("cultureVtables", CultureVtables);
        if (AffiliationAnnualFee is { } fee) w.WriteString("affiliationAnnualFee", $"0x{fee:X}");
        if (AffiliationEnd is { } end) w.WriteString("affiliationEnd", $"0x{end:X}");
        if (AffiliationStartEditable) w.WriteBoolean("affiliationStartEditable", true);
        if (AffiliationFriendlyChance is { } chance) w.WriteString("affiliationFriendlyChance", $"0x{chance:X}");
        if (AffiliationParentWagePercent is { } wage) w.WriteString("affiliationParentWagePercent", $"0x{wage:X}");
        if (AffiliationPlayFriendlies is { } friendlies) w.WriteString("affiliationPlayFriendlies", $"0x{friendlies:X}");
        if (AffiliationFirstOption is { } option) w.WriteString("affiliationFirstOption", $"0x{option:X}");
        if (AffiliationPlayersOnLoan is { } loan) w.WriteString("affiliationPlayersOnLoan", $"0x{loan:X}");
        if (AffiliationScoutingShared is { } scouting) w.WriteString("affiliationScoutingShared", $"0x{scouting:X}");
        w.WriteEndObject();
    }
}

/// <summary>
/// Club → Relationships: a vector (begin/end/cap) of inline 16-byte records stored in the club object itself, same record
/// shape as a person's relations (target, int16 reason, target kind, type, value, flags). Confirmed against FMRTE 27 on
/// Manchester United (37 rows) and Sheffield United (16 rows).
/// </summary>
public sealed record ClubRelationshipOffsets(
    int Vector, int RecordSize, int Target, int Reason, int Kind, int Type, int Value, int Flags,
    int RivalType, int LikesPersonType, int DislikesPersonType, int ClubKind, int PersonKind, int TeamKind)
{
    static readonly string[] Keys =
    [
        "vector", "recordSize", "target", "reason", "kind", "type", "value", "flags",
        "rivalType", "likesPersonType", "dislikesPersonType", "clubKind", "personKind", "teamKind",
    ];

    internal static ClubRelationshipOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"club.relationships.{key}: expected hex offset"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [Vector, RecordSize, Target, Reason, Kind, Type, Value, Flags,
                   RivalType, LikesPersonType, DislikesPersonType, ClubKind, PersonKind, TeamKind];
        w.WriteStartObject("relationships");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>
/// Finances → Sponsors and Other and Debt and Loans: vectors of pointers to records without a vtable, confirmed against
/// FMRTE on Sheffield (13 sponsors, 3 debts) and Everton (6, 3). Only fields with two independent oracles are listed.
/// </summary>
public sealed record ClubFinanceListOffsets(
    int SponsorsPointer, int SponsorSize, int SponsorStart, int SponsorEnd, int SponsorTotal, int SponsorType,
    int DebtsPointer, int DebtsVector, int DebtSize, int DebtOriginal, int DebtEnd, int DebtStart, int DebtSource)
{
    public int? MonthlyRepayment { get; init; }
    public int? MonthlyInterestRepayment { get; init; }
    public int? DebtInterestOnly { get; init; }
    /// <summary>Sponsor record flags: FMRTE's Renew Income (+0x15) and Fixed Value (+0x16), measured 5/11/2026.</summary>
    public int? SponsorRenew { get; init; }
    public int? SponsorFixed { get; init; }
    public ClubStatementOffsets? Statements { get; init; }
    public bool DebtSourceEditable { get; init; }
    public bool DebtOriginalEditable { get; init; }
    public bool DebtDatesEditable { get; init; }
    static readonly string[] Keys =
    [
        "sponsorsPointer", "sponsorSize", "sponsorStart", "sponsorEnd", "sponsorTotal", "sponsorType",
        "debtsPointer", "debtsVector", "debtSize", "debtOriginal", "debtEnd", "debtStart", "debtSource",
    ];

    internal static ClubFinanceListOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"club.financeLists.{key}: expected hex offset"), 16)).ToArray();
        int? Optional(string key) => e.TryGetProperty(key, out var p)
            ? Convert.ToInt32(p.GetString() is { } s && s.StartsWith("0x") ? s[2..]
                : throw new FormatException($"club.financeLists.{key}: expected hex offset"), 16) : null;
        return new ClubFinanceListOffsets(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12])
        { MonthlyRepayment = Optional("monthlyRepayment"), MonthlyInterestRepayment = Optional("monthlyInterestRepayment"),
          DebtInterestOnly = Optional("debtInterestOnly"), SponsorRenew = Optional("sponsorRenew"), SponsorFixed = Optional("sponsorFixed"),
          DebtSourceEditable = e.TryGetProperty("debtSourceEditable", out var editable) && editable.GetBoolean(),
          DebtOriginalEditable = e.TryGetProperty("debtOriginalEditable", out var originalEditable) && originalEditable.GetBoolean(),
          DebtDatesEditable = e.TryGetProperty("debtDatesEditable", out var datesEditable) && datesEditable.GetBoolean(),
          Statements = ClubStatementOffsets.Load(e) };
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [SponsorsPointer, SponsorSize, SponsorStart, SponsorEnd, SponsorTotal, SponsorType,
                   DebtsPointer, DebtsVector, DebtSize, DebtOriginal, DebtEnd, DebtStart, DebtSource];
        w.WriteStartObject("financeLists");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        if (MonthlyRepayment is { } repayment) w.WriteString("monthlyRepayment", $"0x{repayment:X}");
        if (MonthlyInterestRepayment is { } interest) w.WriteString("monthlyInterestRepayment", $"0x{interest:X}");
        if (DebtInterestOnly is { } interestOnly) w.WriteString("debtInterestOnly", $"0x{interestOnly:X}");
        if (SponsorRenew is { } renew) w.WriteString("sponsorRenew", $"0x{renew:X}");
        if (SponsorFixed is { } fixedValue) w.WriteString("sponsorFixed", $"0x{fixedValue:X}");
        if (DebtSourceEditable) w.WriteBoolean("debtSourceEditable", true);
        if (DebtOriginalEditable) w.WriteBoolean("debtOriginalEditable", true);
        if (DebtDatesEditable) w.WriteBoolean("debtDatesEditable", true);
        Statements?.Write(w);
        w.WriteEndObject();
    }
}
