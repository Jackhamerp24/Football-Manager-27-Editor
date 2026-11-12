using System.Globalization;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.App;

public enum ClubSection
{
    Information,
    Squads,
    YouthIntake,
    Finances,
    Supporters,
    TacticalUnderstanding,
    ClubVision,
    Affiliations,
    RegionalDivisions,
    Sponsors,
    Debts,
    IncomeStatement,
    Kits,
    TransferCenter,
    Relationships,
    Staff,
}

public enum ClubDisplayKind
{
    PlainInt,
    FormattedInt,
    Money,
    Rating20,
    Percent,
    Rating100,
    Bool,
    Date,
    Colour,
}

public sealed record ClubCodeOption(long Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record ClubFieldMeta(
    ClubField Field,
    string Label,
    ClubSection Section,
    string Group,
    ClubDisplayKind DisplayKind,
    long Min,
    long Max,
    IReadOnlyDictionary<long, string>? Options = null);

/// <summary>Labels, FMRTE 27 sections/groups, ranges, and value formatting/parsing for <see cref="ClubField"/>.</summary>
public static class ClubFieldInfo
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyDictionary<long, string> EmbargoReasons { get; } = new Dictionary<long, string>
    {
        [0] = "None",
        [4] = "Transfer Ban",
        [16] = "Cannot sign attached players",
    };

    // Codes for the next four lists were traced with Frida while FMRTE 27 saved each choice in turn on Manchester United
    // (Steam 27.0.1, 13/11/2026); names and order are FMRTE's dropdowns. Man Utd was put back afterwards.
    public static IReadOnlyDictionary<long, string> OwnershipTypes { get; } = new Dictionary<long, string>
    {
        [1] = "Limited Liability Company",
        [2] = "Public Limited Company",
        [3] = "Member-Owned (Elections/Ltd)",
        [4] = "Member-Owned (No Elections/Ltd)",
        [5] = "Consortium Private Limited",
        [6] = "Consortium Public Limited",
        [7] = "Member-Owned (Elections/PLC)",
        [8] = "Member-Owned (No Elections/PLC)",
    };

    public static IReadOnlyDictionary<long, string> ChairpersonTitles { get; } = new Dictionary<long, string>
    {
        [1] = "Chairperson",
        [2] = "President",
        [3] = "General Manager",
        [4] = "Founder",
    };

    public static IReadOnlyDictionary<long, string> RichBenefactors { get; } = new Dictionary<long, string>
    {
        [0] = "None",
        [1] = "Foreground",
        [2] = "Background",
        [3] = "Underwriter",
        [4] = "Underwriter (Expects Returns)",
    };

    public static IReadOnlyDictionary<long, string> ClubStatuses { get; } = new Dictionary<long, string>
    {
        [1] = "Professional",
        [2] = "Semi Professional",
        [3] = "Amateur",
    };

    public static string FormatEmbargoReason(long code) =>
        EmbargoReasons.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string FormatOwnershipType(long code) =>
        OwnershipTypes.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string FormatChairpersonTitle(long code) =>
        ChairpersonTitles.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string FormatRichBenefactor(long code) =>
        RichBenefactors.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static IReadOnlyList<ClubCodeOption> OptionsFor(ClubFieldMeta meta, params ReadOnlySpan<long> extraValues)
    {
        if (meta.Options is not { Count: > 0 } opts) return [];
        var map = new SortedDictionary<long, string>();
        foreach (var (code, label) in opts)
            map[code] = label;
        foreach (long extra in extraValues)
        {
            if (!map.ContainsKey(extra))
                map[extra] = extra.ToString(Inv);
        }
        return map.Select(kv => new ClubCodeOption(kv.Key, kv.Value)).ToArray();
    }

    public static IReadOnlyList<ClubSection> ScalarSections { get; } =
    [
        ClubSection.Information,
        ClubSection.Finances,
        ClubSection.Supporters,
        ClubSection.TacticalUnderstanding,
    ];

    public static IReadOnlyList<ClubSection> TopLevelListSections { get; } =
    [
        ClubSection.ClubVision,
        ClubSection.Affiliations,
        ClubSection.RegionalDivisions,
        ClubSection.Kits,
        ClubSection.TransferCenter,
        ClubSection.Relationships,
        ClubSection.Staff,
    ];

    public static IReadOnlyList<ClubSection> FinanceSubSections { get; } =
    [
        ClubSection.Finances,
        ClubSection.Sponsors,
        ClubSection.Debts,
    ];

    public static IReadOnlyList<ClubSection> ListSections { get; } =
    [
        ClubSection.ClubVision,
        ClubSection.Affiliations,
        ClubSection.RegionalDivisions,
        ClubSection.Sponsors,
        ClubSection.Debts,
        ClubSection.Kits,
        ClubSection.TransferCenter,
        ClubSection.Relationships,
        ClubSection.Staff,
    ];

    /// <summary>Club sidebar entries. The per-team Tactical Understanding bars are not offered: only the squad tool's Tactical Understanding button is.</summary>
    public static IReadOnlyList<ClubSection> Sections { get; } =
    [
        ClubSection.Information,
        ClubSection.Squads,
        ClubSection.YouthIntake,
        .. TopLevelListSections.Where(s => s is not ClubSection.RegionalDivisions),
        ClubSection.RegionalDivisions,
        ClubSection.Finances,
        ClubSection.Supporters,
    ];

    public static bool IsListSection(ClubSection section) => section is
        ClubSection.ClubVision or
        ClubSection.Affiliations or
        ClubSection.RegionalDivisions or
        ClubSection.Sponsors or
        ClubSection.Debts or
        ClubSection.Kits or
        ClubSection.TransferCenter or
        ClubSection.Relationships or
        ClubSection.Staff;

    public static string SectionTitle(ClubSection section) => section switch
    {
        ClubSection.Information => "Information",
        ClubSection.Squads => "Squads",
        ClubSection.YouthIntake => "Youth Intake",
        ClubSection.Finances => "Finances",
        ClubSection.Supporters => "Supporters",
        ClubSection.TacticalUnderstanding => "Tactical Understanding",
        ClubSection.ClubVision => "Club Vision",
        ClubSection.Affiliations => "Affiliations",
        ClubSection.RegionalDivisions => "Regional Divisions",
        ClubSection.Sponsors => "Sponsors and Other",
        ClubSection.Debts => "Debt and Loans",
        ClubSection.Kits => "Kits and Colours",
        ClubSection.TransferCenter => "Transfer Center",
        ClubSection.Relationships => "Relationships",
        ClubSection.Staff => "Staff",
        _ => ContractLabels.Spaced(section.ToString()),
    };

    static readonly IReadOnlyList<ClubFieldMeta> Curated =
    [
        // Information / Overview
        new(ClubField.YearFounded, "Year Founded", ClubSection.Information, "Overview", ClubDisplayKind.PlainInt, 0, 2300),
        new(ClubField.Status, "Status", ClubSection.Information, "Overview", ClubDisplayKind.PlainInt, 1, 3, ClubStatuses),
        new(ClubField.ChairmanStatus, "Chairman Status", ClubSection.Information, "Overview", ClubDisplayKind.FormattedInt, 0, 10000),
        new(ClubField.Morale, "Morale", ClubSection.Information, "Overview", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.AverageAttendance, "Average Attendance", ClubSection.Information, "Overview", ClubDisplayKind.FormattedInt, 0, 1_000_000),
        new(ClubField.MinimumAttendance, "Minimum Attendance", ClubSection.Information, "Overview", ClubDisplayKind.FormattedInt, 0, 1_000_000),
        new(ClubField.MaximumAttendance, "Maximum Attendance", ClubSection.Information, "Overview", ClubDisplayKind.FormattedInt, 0, 1_000_000),
        new(ClubField.SeasonTicketHolders, "Season Ticket Holders", ClubSection.Information, "Overview", ClubDisplayKind.FormattedInt, 0, 1_000_000),
        new(ClubField.SocialMediaFollowers, "Social Media Followers", ClubSection.Information, "Overview", ClubDisplayKind.FormattedInt, -1, int.MaxValue),
        new(ClubField.BackgroundColour, "Background Colour", ClubSection.Information, "Overview", ClubDisplayKind.Colour, 0, uint.MaxValue),
        new(ClubField.ForegroundColour, "Foreground Colour", ClubSection.Information, "Overview", ClubDisplayKind.Colour, 0, uint.MaxValue),
        new(ClubField.AllowCustomLogo, "Allow Custom Logo", ClubSection.Information, "Overview", ClubDisplayKind.Bool, 0, 1),
        new(ClubField.OwnershipType, "Ownership Type", ClubSection.Information, "Overview", ClubDisplayKind.PlainInt, 0, 255, OwnershipTypes),
        new(ClubField.ChairpersonTitle, "Chairperson Title", ClubSection.Information, "Overview", ClubDisplayKind.PlainInt, 0, 255, ChairpersonTitles),

        // Information / Teams (Team root)
        new(ClubField.AllowLicensedKits, "Allow Licensed Kits", ClubSection.Information, "Teams", ClubDisplayKind.Bool, 0, 1),
        new(ClubField.TeamReputation, "Reputation", ClubSection.Information, "Teams", ClubDisplayKind.FormattedInt, 0, 10000),

        // Information / Facilities
        new(ClubField.Training, "Training", ClubSection.Information, "Facilities", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.YouthCoaching, "Youth Coaching", ClubSection.Information, "Facilities", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.YouthFacilities, "Youth Facilities", ClubSection.Information, "Facilities", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.YouthRecruitment, "Youth Recruitment", ClubSection.Information, "Facilities", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.YouthImportance, "Youth Importance", ClubSection.Information, "Facilities", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.CorporateFacilities, "Corporate Facilities", ClubSection.Information, "Facilities", ClubDisplayKind.Rating20, 0, 20),

        // Finances / Overview
        new(ClubField.Balance, "Balance", ClubSection.Finances, "Overview", ClubDisplayKind.Money, int.MinValue, int.MaxValue),
        new(ClubField.TransferBudgetSeason, "Transfer Budget (Season)", ClubSection.Finances, "Overview", ClubDisplayKind.Money, int.MinValue, int.MaxValue),
        new(ClubField.TransferBudgetRemaining, "Transfer Budget Remaining", ClubSection.Finances, "Overview", ClubDisplayKind.Money, int.MinValue, int.MaxValue),
        new(ClubField.TransferRevenuePercent, "% of Transfer Revenue Made Available", ClubSection.Finances, "Overview", ClubDisplayKind.Percent, 0, 100),
        new(ClubField.WageBudget, "Wage Budget", ClubSection.Finances, "Overview", ClubDisplayKind.Money, int.MinValue, int.MaxValue),
        new(ClubField.WageUsed, "Wage Used", ClubSection.Finances, "Overview", ClubDisplayKind.Money, 0, int.MaxValue),
        new(ClubField.MaxWage, "Max Wage", ClubSection.Finances, "Overview", ClubDisplayKind.Money, 0, int.MaxValue),
        new(ClubField.RichBenefactor, "Rich Benefactor", ClubSection.Finances, "Overview", ClubDisplayKind.PlainInt, 0, 255, RichBenefactors),
        new(ClubField.AverageTicketPrice, "Average Match Ticket Price", ClubSection.Finances, "Overview", ClubDisplayKind.FormattedInt, 0, 1_000_000),
        new(ClubField.StadiumRentalPerYear, "Stadium Rental Per Year", ClubSection.Finances, "Overview", ClubDisplayKind.Money, 0, int.MaxValue),

        // Finances / Transfer Embargo
        new(ClubField.EmbargoReason, "Reason", ClubSection.Finances, "Transfer Embargo", ClubDisplayKind.PlainInt, 0, 255, EmbargoReasons),
        new(ClubField.EmbargoStart, "Start Date", ClubSection.Finances, "Transfer Embargo", ClubDisplayKind.Date, 0, 23001231),
        new(ClubField.EmbargoEnd, "End Date", ClubSection.Finances, "Transfer Embargo", ClubDisplayKind.Date, 0, 23001231),
        new(ClubField.EmbargoAppealDate, "Appeal Date", ClubSection.Finances, "Transfer Embargo", ClubDisplayKind.Date, 0, 23001231),
        new(ClubField.EmbargoAgeRestriction, "Age Restriction", ClubSection.Finances, "Transfer Embargo", ClubDisplayKind.PlainInt, 0, 255),

        // Supporters / Distribution
        new(ClubField.SupportersHardcore, "Hardcore", ClubSection.Supporters, "Distribution", ClubDisplayKind.Percent, 0, 100),
        new(ClubField.SupportersCore, "Core", ClubSection.Supporters, "Distribution", ClubDisplayKind.Percent, 0, 100),
        new(ClubField.SupportersFamily, "Family", ClubSection.Supporters, "Distribution", ClubDisplayKind.Percent, 0, 100),
        new(ClubField.SupportersFairWeather, "Fair Weather", ClubSection.Supporters, "Distribution", ClubDisplayKind.Percent, 0, 100),
        new(ClubField.SupportersCorporate, "Corporate", ClubSection.Supporters, "Distribution", ClubDisplayKind.Percent, 0, 100),
        new(ClubField.SupportersCasual, "Casual", ClubSection.Supporters, "Distribution", ClubDisplayKind.Percent, 0, 100),

        // Supporters / Profile
        new(ClubField.SupporterLoyalty, "Loyalty", ClubSection.Supporters, "Profile", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.SupporterPassion, "Passion", ClubSection.Supporters, "Profile", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.SupporterPatience, "Patience", ClubSection.Supporters, "Profile", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.SupporterAffluence, "Affluence", ClubSection.Supporters, "Profile", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.SupporterTemperament, "Temperament", ClubSection.Supporters, "Profile", ClubDisplayKind.Rating20, 0, 20),
        new(ClubField.SupporterExpectations, "Expectations", ClubSection.Supporters, "Profile", ClubDisplayKind.Rating20, 0, 20),

        // Tactical Understanding (Team root)
        new(ClubField.PassingStyle, "Passing Style", ClubSection.TacticalUnderstanding, "Tactical Understanding", ClubDisplayKind.Rating100, 0, 100),
        new(ClubField.BuildUp, "Build Up", ClubSection.TacticalUnderstanding, "Tactical Understanding", ClubDisplayKind.Rating100, 0, 100),
        new(ClubField.ChanceCreation, "Chance Creation", ClubSection.TacticalUnderstanding, "Tactical Understanding", ClubDisplayKind.Rating100, 0, 100),
        new(ClubField.LineOfEngagement, "Line of Engagement", ClubSection.TacticalUnderstanding, "Tactical Understanding", ClubDisplayKind.Rating100, 0, 100),
        new(ClubField.PressingIntensity, "Pressing Intensity", ClubSection.TacticalUnderstanding, "Tactical Understanding", ClubDisplayKind.Rating100, 0, 100),
        new(ClubField.PressingTrap, "Pressing Trap", ClubSection.TacticalUnderstanding, "Tactical Understanding", ClubDisplayKind.Rating100, 0, 100),
        new(ClubField.PositionRole, "Position / Role", ClubSection.TacticalUnderstanding, "Tactical Understanding", ClubDisplayKind.Rating100, 0, 100),
    ];

    static readonly IReadOnlyDictionary<ClubField, ClubFieldMeta> ByField = Curated.ToDictionary(m => m.Field);

    /// <summary>Every <see cref="ClubField"/> in FMRTE screen order, including any enum member added later in Core.</summary>
    public static IReadOnlyList<ClubFieldMeta> All { get; } = BuildAll();

    static IReadOnlyList<ClubFieldMeta> BuildAll()
    {
        var list = new List<ClubFieldMeta>(Curated);
        foreach (var field in Enum.GetValues<ClubField>())
        {
            if (ByField.ContainsKey(field)) continue;
            var fallback = Fallback(field, offset: null);
            if (string.Equals(field.ToString(), "Balance", StringComparison.OrdinalIgnoreCase))
            {
                int idx = list.FindIndex(m => m.Field == ClubField.TransferBudgetSeason);
                if (idx >= 0) { list.Insert(idx, fallback); continue; }
            }
            list.Add(fallback);
        }
        return list;
    }

    public static ClubFieldMeta For(ClubField field, ClubFieldOffset? offset = null) =>
        ByField.TryGetValue(field, out var meta) ? meta : Fallback(field, offset);

    /// <summary>Fallback metadata so a new <see cref="ClubField"/> enum member mapped in the profile appears automatically.</summary>
    static ClubFieldMeta Fallback(ClubField field, ClubFieldOffset? offset)
    {
        string name = field.ToString();
        string label = ContractLabels.Spaced(name);
        if (name.Contains("Embargo", StringComparison.OrdinalIgnoreCase))
            return new(field, label.Replace("Embargo ", "", StringComparison.Ordinal), ClubSection.Finances, "Transfer Embargo", ClubDisplayKind.PlainInt, int.MinValue, int.MaxValue);
        if (name.Contains("Balance", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Budget", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Wage", StringComparison.OrdinalIgnoreCase)
            || name.Contains("TicketPrice", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Rental", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Finance", StringComparison.OrdinalIgnoreCase))
            return new(field, label, ClubSection.Finances, "Overview", ClubDisplayKind.Money, int.MinValue, int.MaxValue);
        if (name.StartsWith("Supporters", StringComparison.Ordinal))
            return new(field, ContractLabels.Spaced(name["Supporters".Length..]), ClubSection.Supporters, "Distribution", ClubDisplayKind.Percent, 0, 100);
        if (name.StartsWith("Supporter", StringComparison.Ordinal))
            return new(field, ContractLabels.Spaced(name["Supporter".Length..]), ClubSection.Supporters, "Profile", ClubDisplayKind.Rating20, 0, 20);
        if (offset?.Root == ClubFieldRoot.Team)
            return new(field, label, ClubSection.Information, "Teams", ClubDisplayKind.FormattedInt, int.MinValue, int.MaxValue);
        return new(field, label, ClubSection.Information, "Overview", ClubDisplayKind.FormattedInt, int.MinValue, int.MaxValue);
    }

    public static ClubDisplayKind EffectiveKind(ClubFieldMeta meta, ClubFieldOffset offset) => offset.Type switch
    {
        ClubFieldType.Bool => ClubDisplayKind.Bool,
        ClubFieldType.Date => ClubDisplayKind.Date,
        ClubFieldType.Colour => ClubDisplayKind.Colour,
        _ => meta.DisplayKind is ClubDisplayKind.Bool or ClubDisplayKind.Date or ClubDisplayKind.Colour
            ? ClubDisplayKind.PlainInt
            : meta.DisplayKind,
    };

    public static (long Min, long Max) TypeBounds(ClubFieldOffset offset)
    {
        int scale = Math.Max(1, offset.Scale);
        return offset.Type switch
        {
            ClubFieldType.Bool => (0, 1),
            ClubFieldType.Byte => (0, 255 / scale),
            ClubFieldType.SByte => (-128 / scale, 127 / scale),
            ClubFieldType.Int16 => (short.MinValue / scale, short.MaxValue / scale),
            ClubFieldType.Colour => (0, uint.MaxValue),
            ClubFieldType.Date => (0, 23001231),
            ClubFieldType.Float => (-16_777_216 / scale, 16_777_216 / scale),
            _ => ((long)int.MinValue / scale, (long)int.MaxValue / scale),
        };
    }

    public static (long Min, long Max) EffectiveBounds(ClubFieldMeta meta, ClubFieldOffset offset, long? currentValue = null)
    {
        var (typeMin, typeMax) = TypeBounds(offset);
        if (offset.Type is ClubFieldType.Bool or ClubFieldType.Date or ClubFieldType.Colour)
            return (typeMin, typeMax);

        long min = Math.Clamp(meta.Min, typeMin, typeMax);
        long max = Math.Clamp(meta.Max, min, typeMax);
        if (currentValue is { } cur)
        {
            long safeCur = Math.Clamp(cur, typeMin, typeMax);
            if (safeCur < min) min = safeCur;
            if (safeCur > max) max = safeCur;
        }
        return (min, max);
    }

    public static string FormatFieldValue(ClubField field, long value, ClubDisplayKind? kind = null)
    {
        var meta = For(field);
        if (meta.Options is { Count: > 0 } opts)
            return opts.TryGetValue(value, out var label) ? label : value.ToString(Inv);
        return FormatValue(value, kind ?? meta.DisplayKind);
    }

    public static string FormatValue(long value, ClubDisplayKind kind) => kind switch
    {
        ClubDisplayKind.Bool => value != 0 ? "true" : "false",
        ClubDisplayKind.Date => FormatDate(value),
        ClubDisplayKind.Colour => FormatColour(value),
        ClubDisplayKind.FormattedInt or ClubDisplayKind.Money => value.ToString("N0", Inv),
        _ => value.ToString(Inv),
    };

    static string FormatDate(long yyyymmdd)
    {
        if (yyyymmdd <= 0) return "";
        int year = (int)(yyyymmdd / 10000);
        int month = (int)(yyyymmdd / 100 % 100);
        int day = (int)(yyyymmdd % 100);
        if (year is < 1901 or > 2300 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return "";
        return DateSetting.Format(new DateOnly(year, month, day));
    }

    static string FormatColour(long raw)
    {
        uint u = (uint)Math.Clamp(raw, 0, uint.MaxValue);
        uint alpha = (u >> 24) & 0xFF;
        return alpha is 0xFF or 0x00
            ? $"#{u & 0xFFFFFF:X6}"
            : $"#{u:X8}";
    }

    public static bool TryParseDate(string? text, out long value)
    {
        value = 0;
        string trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed == "1900-01-01")
            return true;
        if (!DateSetting.TryParse(trimmed, out var date))
            return false;
        if (date.Year is < 1901 or > 2300)
            return false;
        value = date.Year * 10000L + date.Month * 100L + date.Day;
        return true;
    }

    /// <summary>Parses #RRGGBB (preserving original top alpha byte) or #AARRGGBB.</summary>
    public static bool TryParseColour(string? text, long originalValue, out long value)
    {
        value = 0;
        string s = text?.Trim() ?? "";
        if (s.StartsWith('#')) s = s[1..];
        else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];

        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, Inv, out uint rgb))
        {
            uint alpha = (uint)Math.Clamp(originalValue, 0, uint.MaxValue) & 0xFF000000u;
            value = alpha | rgb;
            return true;
        }
        if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, Inv, out uint argb))
        {
            value = argb;
            return true;
        }
        return false;
    }

    public static (byte A, byte R, byte G, byte B) SwatchArgb(long raw)
    {
        uint u = (uint)Math.Clamp(raw, 0, uint.MaxValue);
        byte a = (byte)((u >> 24) & 0xFF);
        if (a == 0) a = 0xFF;
        return (a, (byte)((u >> 16) & 0xFF), (byte)((u >> 8) & 0xFF), (byte)(u & 0xFF));
    }
}
