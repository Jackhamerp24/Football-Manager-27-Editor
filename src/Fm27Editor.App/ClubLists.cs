using System.Collections;
using System.Globalization;
using System.Reflection;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed record ClubVisionRow(
    byte TypeCode,
    byte Importance,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? ItemName = null,
    short? ItemIndex = null,
    byte? SourceCode = null,
    string? SourceName = null,
    bool? ForCompetition = null,
    int? Value = null,
    string? TypeName = null,
    int? Target = null,
    int? SourceType = null);

public sealed record ClubAffiliationRow(
    string? ParentClub,
    string? FeederClub,
    byte TypeCode,
    DateOnly? StartDate = null,
    int? ParentClubId = null,
    int? FeederClubId = null,
    DateOnly? EndDate = null,
    int? AnnualFee = null,
    uint? BenefitsMask = null,
    string? TypeName = null);

public sealed record RegionalDivisionRow(
    byte Level,
    string? DivisionName = null,
    int? CompetitionId = null,
    short? Year = null);

public sealed record ClubSponsorRow(
    byte IncomeTypeCode,
    int TotalValue,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int? AnnualValue = null,
    string? IncomeTypeName = null);

public sealed record ClubDebtRow(
    byte SourceCode,
    int OriginalDebt,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int? Instalment = null,
    string? SourceName = null);

public sealed record ClubKitRow(
    uint Foreground,
    uint Background,
    uint Outline,
    uint NumberColour,
    uint NumberOutline,
    byte Style,
    byte? KitKindCode = null,
    string? KitKindName = null,
    byte? ItemTypeCode = null,
    string? ItemTypeName = null,
    byte? PlayerTypeCode = null,
    string? PlayerTypeName = null);

public sealed record ClubTransferOfferRow(
    int OfferId,
    string? PersonName = null,
    int? PersonId = null,
    string? SellerClub = null,
    int? SellerClubId = null,
    string? BuyerClub = null,
    int? BuyerClubId = null,
    int? Fee = null,
    byte? StatusCode = null,
    string? StatusName = null,
    string? TypeName = null);

public sealed record ClubListColumn(
    string Header,
    double Width,
    bool IsStar = false,
    bool RightAlign = false);

public sealed record ClubListCell(
    string Text,
    bool RightAlign = false,
    uint? SwatchArgb = null);

public sealed record ClubListDetailField(
    string Label,
    string Value,
    uint? SwatchArgb = null,
    bool? BoolValue = null);

public sealed record ClubListRowModel(
    IReadOnlyList<ClubListCell> Cells,
    IReadOnlyList<ClubListDetailField> Details)
{
    public string AutomationName =>
        string.Join(", ", Cells.Select(c => c.Text).Where(t => !string.IsNullOrEmpty(t)));

    public override string ToString() => AutomationName;
}

public sealed record ClubListTableModel(
    ClubSection Section,
    string Title,
    bool HasReader,
    bool UsesTeamRoot,
    IReadOnlyList<ClubListColumn> Columns,
    IReadOnlyList<ClubListRowModel> Rows)
{
    public static ClubListTableModel Empty(ClubSection section, bool hasReader = false, bool usesTeamRoot = false) =>
        new(section, ClubFieldInfo.SectionTitle(section), hasReader, usesTeamRoot, [], []);
}

/// <summary>Oracle-verified FMRTE 27 code labels and value formatters for the 7 Club list sections.</summary>
public static class ClubListLabels
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static IReadOnlyDictionary<byte, string> VisionTypes { get; } = new Dictionary<byte, string>
    {
        [3] = "Win promotion",
        [6] = "Reach the playoffs",
        [33] = "Be competitive",
        [40] = "Become self-sustainable",        [41] = "Repair the club's financial damage",
        [43] = "Sign young players to develop for profit",
        [46] = "Spend the original transfer budget",
        [80] = "Break even in the transfer market",
        [83] = "Maximum three-year contracts for players over the age of X",
        [85] = "Sell players before buying",
        [86] = "Cannot sign non-Basque players",
        [87] = "Can only sign African players",
        [88] = "Cannot sign non-Nation players",
        [89] = "Cannot sign non-Nation goalkeepers",
        [91] = "Cannot sign players who cannot speak Welsh",
        [92] = "Cannot sign any players",
        [100] = "Sign Players Of Nationality",
        [102] = "Sign players from the lower levels of the domestic game",
        [106] = "Sign players from domestic rivals",
        [123] = "Develop the best youth system in the world",
        [124] = "Develop players for the main club",
        [125] = "Give playing time to first team players",
        [151] = "Play defensively solid football",
        [154] = "Make the most of set pieces",
        [156] = "Play counter-attacking football",
        [170] = "Build a new stadium",
        [171] = "Expand stadium",
        [172] = "Buy current stadium",
        [42] = "Increase commercial revenue",
        [44] = "Work within wage budget",
        [104] = "Sign players under the age of X",
        [120] = "Develop players using the club's youth system",
        [150] = "Play attacking football",
        [155] = "Play entertaining football",
    };

    public static IReadOnlyDictionary<byte, string> VisionSources { get; } = new Dictionary<byte, string>
    {
        [0] = "Board",
    };

    public static IReadOnlyDictionary<byte, string> AffiliationTypes { get; } = new Dictionary<byte, string>
    {
        [16] = "Good Relations",
        [25] = "Women's Club",
    };

    public static IReadOnlyDictionary<byte, string> SponsorIncomeTypes { get; } = new Dictionary<byte, string>
    {
        [1] = "Kit Sponsor",
        [3] = "Stadium Sponsor",
        [4] = "General Sponsor",
        [6] = "Other Income",
        [11] = "Back of Shirt",
        [13] = "Training Kit",
        [18] = "Total Commercial Income",
        [19] = "Shirt Sleeve",
        [22] = "Kit Manufacturer",
        [23] = "Stadium",
    };

    public static IReadOnlyDictionary<byte, string> DebtSources { get; } = new Dictionary<byte, string>
    {
        [1] = "Bank",
        [5] = "State",
        [10] = "Loan Repayable On Promotion To Top Division",
        [15] = "Miscellaneous",
    };

    public static IReadOnlyDictionary<byte, string> KitKinds { get; } = new Dictionary<byte, string>
    {
        [0] = "Home",
        [1] = "Away",
        [2] = "Third",
    };

    public static IReadOnlyDictionary<byte, string> TransferOfferStatuses { get; } = new Dictionary<byte, string>
    {
        [8] = "Player accepted",
        [10] = "Unknown status 1",
        [12] = "Ready to sign",
        [19] = "Confirmed",
    };

    public static string VisionType(byte code) =>
        VisionTypes.TryGetValue(code, out var name) ? name : $"Unknown ({code.ToString(Inv)})";

    public static string VisionSource(byte code) =>
        VisionSources.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string AffiliationType(byte code) =>
        AffiliationTypes.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string SponsorIncomeType(byte code) =>
        SponsorIncomeTypes.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string DebtSource(byte code) =>
        DebtSources.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string KitKind(byte code) =>
        KitKinds.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static string TransferOfferStatus(byte code) =>
        TransferOfferStatuses.TryGetValue(code, out var name) ? name : code.ToString(Inv);

    public static bool IsRealDate(DateOnly? date) => date is { Year: > 1900 };

    public static string FormatDate(DateOnly? date) =>
        date is { Year: > 1900 } d ? DateSetting.Format(d) : "";
}

/// <summary>Builds master list tables and selected-row detail panels without reading process memory directly.</summary>
public static class ClubListBuilder
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static ClubListTableModel BuildVision(IEnumerable<ClubVisionRow> rows, bool hasReader = true, bool usesTeamRoot = false)
    {
        var list = rows.ToArray();
        bool hasSource = list.Any(r => !string.IsNullOrWhiteSpace(r.SourceName) || r.SourceCode is not null || r.SourceType is not null);
        bool hasForComp = list.Any(r => r.ForCompetition is not null);

        var cols = new List<ClubListColumn>(6) { new("Type", 2.2, IsStar: true) };
        if (hasSource) cols.Add(new("Source", 106));
        cols.Add(new("Importance", 97, RightAlign: true));
        cols.Add(new("Item", 1.4, IsStar: true));
        if (hasForComp) cols.Add(new("For Comp.", 90));
        cols.Add(new("Start", 113, RightAlign: true));
        cols.Add(new("End", 113, RightAlign: true));

        var models = new List<ClubListRowModel>(list.Length);
        foreach (var r in list)
        {
            string typeText = !string.IsNullOrWhiteSpace(r.TypeName)
                ? r.TypeName
                : ClubListLabels.VisionType(r.TypeCode);
            if (r.Target is { } tgt && tgt > 0 && typeText.Contains("X"))
                typeText = typeText.Replace("X", tgt.ToString(Inv));

            string sourceText = !string.IsNullOrWhiteSpace(r.SourceName)
                ? r.SourceName
                : r.SourceType == 1 ? "Board"
                : r.SourceType is { } other ? $"Unknown ({other.ToString(Inv)})"
                : r.SourceCode is { } sc ? (sc == 1 ? "Board" : (ClubListLabels.VisionSources.TryGetValue(sc, out var sn) ? sn : "Other source")) : "";
            string importanceText = r.Importance.ToString(Inv);
            string itemText = !string.IsNullOrWhiteSpace(r.ItemName)
                ? r.ItemName
                : r.ItemIndex is { } idx and >= 0 ? idx.ToString(Inv) : "";
            string forCompText = r.ForCompetition is { } fc ? (fc ? "True" : "False") : "";
            string startText = ClubListLabels.FormatDate(r.StartDate);
            string endText = ClubListLabels.FormatDate(r.EndDate);

            var cells = new List<ClubListCell>(cols.Count) { new(typeText) };
            if (hasSource) cells.Add(new(sourceText));
            cells.Add(new(importanceText, RightAlign: true));
            cells.Add(new(itemText));
            if (hasForComp) cells.Add(new(forCompText));
            cells.Add(new(startText, RightAlign: true));
            cells.Add(new(endText, RightAlign: true));

            var details = new List<ClubListDetailField>(8)
            {
                new("Type", typeText),
            };
            if (sourceText.Length > 0) details.Add(new("Source", sourceText));
            details.Add(new("Importance", importanceText));
            if (itemText.Length > 0) details.Add(new("Item", itemText));
            if (r.ForCompetition is { } forComp)
                details.Add(new("For Competition", forComp ? "True" : "False", BoolValue: forComp));
            if (r.Value is { } val and >= 0)
                details.Add(new("Value", val.ToString("N0", Inv)));
            details.Add(new("Start Date", startText));
            details.Add(new("End Date", endText));

            models.Add(new ClubListRowModel(cells, details));
        }

        return new ClubListTableModel(
            ClubSection.ClubVision,
            ClubFieldInfo.SectionTitle(ClubSection.ClubVision),
            hasReader,
            usesTeamRoot,
            cols,
            models);
    }

    public static ClubListTableModel BuildAffiliations(IEnumerable<ClubAffiliationRow> rows, bool hasReader = true)
    {
        var list = rows.ToArray();
        bool hasEnd = list.Any(r => ClubListLabels.IsRealDate(r.EndDate));
        bool hasFee = list.Any(r => r.AnnualFee is not null);

        var cols = new List<ClubListColumn>(6)
        {
            new("Parent Club", 1.3, IsStar: true),
            new("Feeder Club", 1.5, IsStar: true),
            new("Type", 1.1, IsStar: true),
            new("Start", 113, RightAlign: true),
        };
        if (hasEnd) cols.Add(new("End", 113, RightAlign: true));
        if (hasFee) cols.Add(new("Annual Fee", 120, RightAlign: true));

        var models = new List<ClubListRowModel>(list.Length);
        foreach (var r in list)
        {
            string parentText = FormatClubLabel(r.ParentClub, r.ParentClubId);
            string feederText = FormatClubLabel(r.FeederClub, r.FeederClubId);
            string typeText = !string.IsNullOrWhiteSpace(r.TypeName) ? r.TypeName : ClubListLabels.AffiliationType(r.TypeCode);
            string startText = ClubListLabels.FormatDate(r.StartDate);
            string endText = ClubListLabels.FormatDate(r.EndDate);
            string feeText = r.AnnualFee is { } fee ? fee.ToString("N0", Inv) : "";

            var cells = new List<ClubListCell>(cols.Count)
            {
                new(parentText),
                new(feederText),
                new(typeText),
                new(startText, RightAlign: true),
            };
            if (hasEnd) cells.Add(new(endText, RightAlign: true));
            if (hasFee) cells.Add(new(feeText, RightAlign: true));

            var details = new List<ClubListDetailField>(9);
            if (parentText.Length > 0) details.Add(new("Parent Club", parentText));
            if (r.ParentClubId is { } pid && !string.IsNullOrWhiteSpace(r.ParentClub))
                details.Add(new("Parent Club ID", pid.ToString(Inv)));
            if (feederText.Length > 0) details.Add(new("Feeder Club", feederText));
            if (r.FeederClubId is { } fid && !string.IsNullOrWhiteSpace(r.FeederClub))
                details.Add(new("Feeder Club ID", fid.ToString(Inv)));
            details.Add(new("Type", typeText));
            details.Add(new("Start Date", startText));
            if (r.EndDate is not null) details.Add(new("End Date", endText));
            if (r.AnnualFee is { } af) details.Add(new("Annual Fee", af.ToString("N0", Inv)));
            if (r.BenefitsMask is { } mask) details.Add(new("Benefits Mask", $"0x{mask:X}"));

            models.Add(new ClubListRowModel(cells, details));
        }

        return new ClubListTableModel(
            ClubSection.Affiliations,
            ClubFieldInfo.SectionTitle(ClubSection.Affiliations),
            hasReader,
            UsesTeamRoot: false,
            cols,
            models);
    }

    public static ClubListTableModel BuildRegionalDivisions(IEnumerable<RegionalDivisionRow> rows, bool hasReader = true)
    {
        var list = rows.ToArray();
        bool hasDivision = list.Any(r => !string.IsNullOrWhiteSpace(r.DivisionName));
        bool hasId = list.Any(r => r.CompetitionId is not null);
        bool hasYear = list.Any(r => r.Year is not null);

        var cols = new List<ClubListColumn>(4);
        if (hasDivision) cols.Add(new("Division", 2.4, IsStar: true));
        if (hasId) cols.Add(new("ID", 110, RightAlign: true));
        cols.Add(new("Level", 81, RightAlign: true));
        if (hasYear) cols.Add(new("Year", 81, RightAlign: true));

        var models = new List<ClubListRowModel>(list.Length);
        foreach (var r in list)
        {
            string divisionText = !string.IsNullOrWhiteSpace(r.DivisionName) ? r.DivisionName : "";
            string idText = r.CompetitionId is { } cid ? cid.ToString(Inv) : "";
            string levelText = r.Level.ToString(Inv);
            string yearText = r.Year is { } yr ? yr.ToString(Inv) : "";

            var cells = new List<ClubListCell>(cols.Count);
            if (hasDivision) cells.Add(new(divisionText));
            if (hasId) cells.Add(new(idText, RightAlign: true));
            cells.Add(new(levelText, RightAlign: true));
            if (hasYear) cells.Add(new(yearText, RightAlign: true));

            var details = new List<ClubListDetailField>(4);
            if (!string.IsNullOrWhiteSpace(r.DivisionName))
                details.Add(new("Division", r.DivisionName));
            if (r.CompetitionId is { } compId)
                details.Add(new("Competition ID", compId.ToString(Inv)));
            details.Add(new("Level", levelText));
            if (r.Year is { } y)
                details.Add(new("Year", y.ToString(Inv)));

            models.Add(new ClubListRowModel(cells, details));
        }

        return new ClubListTableModel(
            ClubSection.RegionalDivisions,
            ClubFieldInfo.SectionTitle(ClubSection.RegionalDivisions),
            hasReader,
            UsesTeamRoot: false,
            cols,
            models);
    }

    public static ClubListTableModel BuildSponsors(IEnumerable<ClubSponsorRow> rows, bool hasReader = true)
    {
        var list = rows.ToArray();
        var cols = new ClubListColumn[]
        {
            new("Income Type", 1.8, IsStar: true),
            new("Total Value", 143, RightAlign: true),
            new("Start", 113, RightAlign: true),
            new("End", 113, RightAlign: true),
        };

        var models = new List<ClubListRowModel>(list.Length);
        foreach (var r in list)
        {
            string typeText = !string.IsNullOrWhiteSpace(r.IncomeTypeName)
                ? r.IncomeTypeName
                : ClubListLabels.SponsorIncomeType(r.IncomeTypeCode);
            string totalText = r.TotalValue.ToString("N0", Inv);
            string startText = ClubListLabels.FormatDate(r.StartDate);
            string endText = ClubListLabels.FormatDate(r.EndDate);

            var cells = new ClubListCell[]
            {
                new(typeText),
                new(totalText, RightAlign: true),
                new(startText, RightAlign: true),
                new(endText, RightAlign: true),
            };

            var details = new List<ClubListDetailField>(5)
            {
                new("Income Type", typeText),
                new("Total Value", totalText),
            };
            if (r.AnnualValue is { } annual)
                details.Add(new("Annual Value", annual.ToString("N0", Inv)));
            details.Add(new("Start Date", startText));
            details.Add(new("End Date", endText));

            models.Add(new ClubListRowModel(cells, details));
        }

        return new ClubListTableModel(
            ClubSection.Sponsors,
            ClubFieldInfo.SectionTitle(ClubSection.Sponsors),
            hasReader,
            UsesTeamRoot: false,
            cols,
            models);
    }

    public static ClubListTableModel BuildDebts(IEnumerable<ClubDebtRow> rows, bool hasReader = true)
    {
        var list = rows.ToArray();
        var cols = new ClubListColumn[]
        {
            new("Source", 2.0, IsStar: true),
            new("Original Debt", 147, RightAlign: true),
            new("Start", 113, RightAlign: true),
            new("End", 113, RightAlign: true),
        };

        var models = new List<ClubListRowModel>(list.Length);
        foreach (var r in list)
        {
            string sourceText = !string.IsNullOrWhiteSpace(r.SourceName)
                ? r.SourceName
                : ClubListLabels.DebtSource(r.SourceCode);
            string debtText = r.OriginalDebt.ToString("N0", Inv);
            string startText = ClubListLabels.FormatDate(r.StartDate);
            string endText = ClubListLabels.FormatDate(r.EndDate);

            var cells = new ClubListCell[]
            {
                new(sourceText),
                new(debtText, RightAlign: true),
                new(startText, RightAlign: true),
                new(endText, RightAlign: true),
            };

            var details = new List<ClubListDetailField>(5)
            {
                new("Source", sourceText),
                new("Original Debt", debtText),
            };
            if (r.Instalment is { } inst)
                details.Add(new("Instalment", inst.ToString("N0", Inv)));
            details.Add(new("Start Date", startText));
            details.Add(new("End Date", endText));

            models.Add(new ClubListRowModel(cells, details));
        }

        return new ClubListTableModel(
            ClubSection.Debts,
            ClubFieldInfo.SectionTitle(ClubSection.Debts),
            hasReader,
            UsesTeamRoot: false,
            cols,
            models);
    }

    public static ClubListTableModel BuildKits(IEnumerable<ClubKitRow> rows, bool hasReader = true)
    {
        var list = rows.ToArray();
        bool hasKit = list.Any(r => !string.IsNullOrWhiteSpace(r.KitKindName) || r.KitKindCode is not null);
        bool hasItem = list.Any(r => !string.IsNullOrWhiteSpace(r.ItemTypeName) || r.ItemTypeCode is not null);

        var cols = new List<ClubListColumn>(6);
        if (hasKit) cols.Add(new("Kit", 97));
        if (hasItem) cols.Add(new("Item", 97));
        cols.Add(new("Foreground", 1.0, IsStar: true));
        cols.Add(new("Background", 1.0, IsStar: true));
        cols.Add(new("Outline", 1.0, IsStar: true));
        cols.Add(new("Style", 74, RightAlign: true));

        var models = new List<ClubListRowModel>(list.Length);
        foreach (var r in list)
        {
            string kitText = !string.IsNullOrWhiteSpace(r.KitKindName)
                ? r.KitKindName
                : r.KitKindCode is { } kc ? ClubListLabels.KitKind(kc) : "";
            string itemText = !string.IsNullOrWhiteSpace(r.ItemTypeName)
                ? r.ItemTypeName
                : r.ItemTypeCode is { } ic ? ic.ToString(Inv) : "";
            string playerTypeText = !string.IsNullOrWhiteSpace(r.PlayerTypeName)
                ? r.PlayerTypeName
                : r.PlayerTypeCode is { } pc ? pc.ToString(Inv) : "";

            string fgText = ClubFieldInfo.FormatValue(r.Foreground, ClubDisplayKind.Colour);
            string bgText = ClubFieldInfo.FormatValue(r.Background, ClubDisplayKind.Colour);
            string outText = ClubFieldInfo.FormatValue(r.Outline, ClubDisplayKind.Colour);
            string numText = ClubFieldInfo.FormatValue(r.NumberColour, ClubDisplayKind.Colour);
            string numOutText = ClubFieldInfo.FormatValue(r.NumberOutline, ClubDisplayKind.Colour);
            string styleText = r.Style.ToString(Inv);

            var cells = new List<ClubListCell>(cols.Count);
            if (hasKit) cells.Add(new(kitText));
            if (hasItem) cells.Add(new(itemText));
            cells.Add(new(fgText, SwatchArgb: r.Foreground));
            cells.Add(new(bgText, SwatchArgb: r.Background));
            cells.Add(new(outText, SwatchArgb: r.Outline));
            cells.Add(new(styleText, RightAlign: true));

            var details = new List<ClubListDetailField>(9);
            if (kitText.Length > 0) details.Add(new("Kit", kitText));
            if (itemText.Length > 0) details.Add(new("Item", itemText));
            if (playerTypeText.Length > 0) details.Add(new("Player Type", playerTypeText));
            details.Add(new("Foreground", fgText, SwatchArgb: r.Foreground));
            details.Add(new("Background", bgText, SwatchArgb: r.Background));
            details.Add(new("Outline", outText, SwatchArgb: r.Outline));
            details.Add(new("Number", numText, SwatchArgb: r.NumberColour));
            details.Add(new("Number Outline", numOutText, SwatchArgb: r.NumberOutline));
            details.Add(new("Item Style", styleText));

            models.Add(new ClubListRowModel(cells, details));
        }

        return new ClubListTableModel(
            ClubSection.Kits,
            ClubFieldInfo.SectionTitle(ClubSection.Kits),
            hasReader,
            UsesTeamRoot: false,
            cols,
            models);
    }

    public static ClubListTableModel BuildTransferCenter(IEnumerable<ClubTransferOfferRow> rows, bool hasReader = true)
    {
        var list = rows.ToArray();
        bool hasStatus = list.Any(r => !string.IsNullOrWhiteSpace(r.StatusName) || r.StatusCode is not null);
        bool hasType = list.Any(r => !string.IsNullOrWhiteSpace(r.TypeName));

        var cols = new List<ClubListColumn>(5)
        {
            new("Person", 1.4, IsStar: true),
            new("From", 1.2, IsStar: true),
            new("To", 1.2, IsStar: true),
            new("Fee", 129, RightAlign: true),
        };
        if (hasType) cols.Insert(0, new("Type", 98));
        if (hasStatus) cols.Add(new("Status", 1.1, IsStar: true));

        var models = new List<ClubListRowModel>(list.Length);
        foreach (var r in list)
        {
            string personText = !string.IsNullOrWhiteSpace(r.PersonName)
                ? r.PersonName
                : r.PersonId is { } pid ? pid.ToString(Inv) : r.OfferId.ToString(Inv);
            string fromText = FormatClubLabel(r.SellerClub, r.SellerClubId);
            string toText = FormatClubLabel(r.BuyerClub, r.BuyerClubId);
            string feeText = r.Fee is { } fee ? fee.ToString("N0", Inv) : "";
            string statusText = !string.IsNullOrWhiteSpace(r.StatusName)
                ? r.StatusName
                : r.StatusCode is { } sc ? ClubListLabels.TransferOfferStatus(sc) : "";

            var cells = new List<ClubListCell>(cols.Count)
            {
                new(personText),
                new(fromText),
                new(toText),
                new(feeText, RightAlign: true),
            };
            if (hasType) cells.Insert(0, new(r.TypeName ?? ""));
            if (hasStatus) cells.Add(new(statusText));

            var details = new List<ClubListDetailField>(8)
            {
                new("Offer ID", r.OfferId.ToString(Inv)),
            };
            if (!string.IsNullOrWhiteSpace(r.PersonName))
                details.Add(new("Person", r.PersonName));
            if (r.PersonId is { } personId)
                details.Add(new("Person ID", personId.ToString(Inv)));
            if (fromText.Length > 0)
                details.Add(new("From", fromText));
            if (r.SellerClubId is { } sellerId && !string.IsNullOrWhiteSpace(r.SellerClub))
                details.Add(new("From Club ID", sellerId.ToString(Inv)));
            if (toText.Length > 0)
                details.Add(new("To", toText));
            if (r.BuyerClubId is { } buyerId && !string.IsNullOrWhiteSpace(r.BuyerClub))
                details.Add(new("To Club ID", buyerId.ToString(Inv)));
            if (r.Fee is { } f)
                details.Add(new("Fee", f.ToString("N0", Inv)));
            if (statusText.Length > 0)
                details.Add(new("Status", statusText));

            models.Add(new ClubListRowModel(cells, details));
        }

        return new ClubListTableModel(
            ClubSection.TransferCenter,
            ClubFieldInfo.SectionTitle(ClubSection.TransferCenter),
            hasReader,
            UsesTeamRoot: false,
            cols,
            models);
    }

    static string FormatClubLabel(string? name, int? id) =>
        !string.IsNullOrWhiteSpace(name) ? name : id is { } v ? v.ToString(Inv) : "";
}

public interface IClubListProvider
{
    bool HasReader(object session, ClubSection section);
    ClubListTableModel ReadSection(
        ClubSection section,
        object session,
        ClubEntry club,
        TeamEntry? selectedTeam,
        IReadOnlyList<ClubEntry> clubs,
        IReadOnlyDictionary<int, CompetitionInfo>? competitions,
        IReadOnlyList<PlayerRecord> players);
}

/// <summary>
/// Resolves Core list readers on <c>GameSession</c> (or <c>session.ClubLists</c>) without touching process memory.
/// Until Core exposes a reader for a list section, <see cref="HasReader"/> is <see langword="false"/> and the section renders empty.
/// </summary>
public sealed class GameSessionClubListProvider : IClubListProvider
{
    public static GameSessionClubListProvider Default { get; } = new();

    static readonly IReadOnlyDictionary<ClubSection, string[]> MethodCandidates = new Dictionary<ClubSection, string[]>
    {
        [ClubSection.ClubVision] = ["ReadClubVision", "ReadVision", "ClubVision", "Vision"],
        [ClubSection.Affiliations] = ["ReadAffiliations", "ReadClubAffiliations", "Affiliations"],
        [ClubSection.RegionalDivisions] = ["ReadRegionalDivisions", "ReadClubRegionalDivisions", "RegionalDivisions"],
        [ClubSection.Sponsors] = ["ReadSponsors", "ReadClubSponsors", "ReadSponsorsAndOther", "Sponsors"],
        [ClubSection.Debts] = ["ReadDebts", "ReadClubDebts", "ReadDebtAndLoans", "Debts"],
        [ClubSection.Kits] = ["ReadKits", "ReadClubKits", "ReadKitsAndColours", "Kits"],
        [ClubSection.TransferCenter] = ["ReadTransferCenter", "ReadClubTransferCenter", "ReadClubTransferOffers", "TransferCenter"],
    };

    public bool HasReader(object session, ClubSection section) =>
        ResolveMethod(session, section) is not null;

    public bool NeedsCompetitions(object session, ClubSection section)
    {
        if (ResolveMethod(session, section) is not { } resolved) return false;
        return resolved.Method.GetParameters().Any(p =>
            p.ParameterType.IsAssignableFrom(typeof(IReadOnlyDictionary<int, CompetitionInfo>)));
    }

    public ClubListTableModel ReadSection(
        ClubSection section,
        object session,
        ClubEntry club,
        TeamEntry? selectedTeam,
        IReadOnlyList<ClubEntry> clubs,
        IReadOnlyDictionary<int, CompetitionInfo>? competitions,
        IReadOnlyList<PlayerRecord> players)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(club);

        if (ResolveMethod(session, section) is not { } resolved)
            return ClubListTableModel.Empty(section, hasReader: false);

        bool usesTeamRoot = MethodUsesTeam(resolved.Method);
        if (!TryBindArguments(section, resolved.Method, club, selectedTeam, clubs, competitions, players, out var args))
            return ClubListTableModel.Empty(section, hasReader: true, usesTeamRoot);

        if (resolved.Method.Invoke(resolved.Target, args) is not IEnumerable rawItems)
            return ClubListTableModel.Empty(section, hasReader: true, usesTeamRoot);

        var items = rawItems.Cast<object?>().Where(x => x is not null).Cast<object>().ToArray();
        var clubsByIndex = BuildClubsById(clubs);

        return section switch
        {
            ClubSection.ClubVision => ClubListBuilder.BuildVision(
                items.Select(x => x as ClubVisionRow ?? MapVisionRow(x)),
                hasReader: true,
                usesTeamRoot),
            ClubSection.Affiliations => ClubListBuilder.BuildAffiliations(
                items.Select(x => x as ClubAffiliationRow ?? MapAffiliationRow(x, clubsByIndex)),
                hasReader: true),
            ClubSection.RegionalDivisions => ClubListBuilder.BuildRegionalDivisions(
                items.Select(x => x as RegionalDivisionRow ?? MapRegionalDivisionRow(x)),
                hasReader: true),
            ClubSection.Sponsors => ClubListBuilder.BuildSponsors(
                items.Select(x => x as ClubSponsorRow ?? MapSponsorRow(x)),
                hasReader: true),
            ClubSection.Debts => ClubListBuilder.BuildDebts(
                items.Select(x => x as ClubDebtRow ?? MapDebtRow(x)),
                hasReader: true),
            ClubSection.Kits => ClubListBuilder.BuildKits(
                items.Select(x => x as ClubKitRow ?? MapKitRow(x)),
                hasReader: true),
            ClubSection.TransferCenter => ClubListBuilder.BuildTransferCenter(
                items.Select(x => x as ClubTransferOfferRow ?? MapTransferOfferRow(x, clubsByIndex)),
                hasReader: true),
            _ => ClubListTableModel.Empty(section, hasReader: false),
        };
    }

    static (object Target, MethodInfo Method)? ResolveMethod(object session, ClubSection section)
    {
        if (!MethodCandidates.TryGetValue(section, out var names))
            return null;

        if (FindMethodOnTarget(session, names) is { } direct)
            return (session, direct);

        var subProp = session.GetType().GetProperty("ClubLists", BindingFlags.Instance | BindingFlags.Public);
        if (subProp?.GetIndexParameters().Length == 0 && subProp.GetValue(session) is { } subTarget
            && FindMethodOnTarget(subTarget, names) is { } nested)
        {
            return (subTarget, nested);
        }

        return null;
    }

    static MethodInfo? FindMethodOnTarget(object target, string[] candidateNames)
    {
        var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (string name in candidateNames)
        {
            var match = methods.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.Ordinal)
                && typeof(IEnumerable).IsAssignableFrom(m.ReturnType)
                && m.ReturnType != typeof(string));
            if (match is not null)
                return match;
        }
        return null;
    }

    static bool MethodUsesTeam(MethodInfo method) =>
        method.GetParameters().Any(p =>
            p.ParameterType == typeof(TeamEntry)
            || (p.ParameterType == typeof(ulong)
                && p.Name is not null
                && p.Name.Contains("team", StringComparison.OrdinalIgnoreCase)));

    static bool TryBindArguments(
        ClubSection section,
        MethodInfo method,
        ClubEntry club,
        TeamEntry? selectedTeam,
        IReadOnlyList<ClubEntry> clubs,
        IReadOnlyDictionary<int, CompetitionInfo>? competitions,
        IReadOnlyList<PlayerRecord> players,
        out object?[] args)
    {
        var parameters = method.GetParameters();
        args = new object?[parameters.Length];
        int ulongSeen = 0;

        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var pt = p.ParameterType;
            string pName = p.Name ?? "";

            if (pt == typeof(ClubEntry))
            {
                args[i] = club;
            }
            else if (pt == typeof(TeamEntry))
            {
                var team = selectedTeam ?? ClubCatalog.DefaultTeam(club);
                if (team is null && !IsNullableParameter(p)) return false;
                args[i] = team;
            }
            else if (pt == typeof(ulong))
            {
                bool wantsTeam = pName.Contains("team", StringComparison.OrdinalIgnoreCase)
                                 || (ulongSeen > 0 && !pName.Contains("club", StringComparison.OrdinalIgnoreCase));
                ulongSeen++;
                if (wantsTeam)
                {
                    ulong teamAddr = (selectedTeam ?? ClubCatalog.DefaultTeam(club))?.Address ?? 0;
                    if (teamAddr == 0) return false;
                    args[i] = teamAddr;
                }
                else
                {
                    if (club.Address == 0) return false;
                    args[i] = club.Address;
                }
            }
            else if (pt == typeof(int) && (pName.Contains("club", StringComparison.OrdinalIgnoreCase) || string.Equals(pName, "id", StringComparison.OrdinalIgnoreCase)))
            {
                if (club.Id is not { } cid) return false;
                args[i] = cid;
            }
            else if (pt.IsAssignableFrom(typeof(IReadOnlyList<ClubEntry>)))
            {
                args[i] = clubs;
            }
            else if (pt.IsAssignableFrom(typeof(IReadOnlyDictionary<int, CompetitionInfo>)))
            {
                args[i] = competitions ?? new Dictionary<int, CompetitionInfo>();
            }
            else if (pt.IsAssignableFrom(typeof(IReadOnlyList<PlayerRecord>)))
            {
                args[i] = players;
            }
            else if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
            }
            else if (IsNullableParameter(p))
            {
                args[i] = null;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    static bool IsNullableParameter(ParameterInfo p) =>
        !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) is not null;

    static Dictionary<int, string> BuildClubsById(IReadOnlyList<ClubEntry> clubs)
    {
        var map = new Dictionary<int, string>();
        foreach (var c in clubs)
        {
            if (c.Id is { } id && (!string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.ShortName)))
                map.TryAdd(id, !string.IsNullOrWhiteSpace(c.Name) ? c.Name : c.ShortName!);
        }
        return map;
    }

    static ClubVisionRow MapVisionRow(object item)
    {
        var (typeCode, typeEnumLabel) = ReadCodeAndEnumLabel(item, ClubListLabels.VisionTypes, ["TypeCode", "Type", "Kind"]);
        string? explicitType = ReadString(item, ["TypeName", "Description", "Label"]);
        string? typeName = explicitType ?? (typeEnumLabel is not null && !typeEnumLabel.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) ? typeEnumLabel : null);

        byte importance = ReadByte(item, ["Importance"]) ?? 0;
        var start = ReadDate(item, ["StartDate", "Start", "Starts"]);
        var end = ReadDate(item, ["EndDate", "End", "Ends", "Expires"]);

        string? itemName = ReadString(item, ["ItemName"])
                           ?? ReadEntityName(item, ["Item", "Club", "ClubItem", "ItemClub", "Rival", "Competition", "ItemCompetition"]);
        int? rawIdx = ReadInt(item, ["ItemIndex", "ClubItemIndex", "ClubIndex", "CompetitionIndex"]);
        short? itemIdx = rawIdx is { } idx and >= 0 and <= short.MaxValue ? (short)idx : null;

        var (sourceCode, sourceEnumLabel) = ReadOptionalCodeAndEnumLabel(item, ClubListLabels.VisionSources, ["SourceCode", "Source"]);
        string? sourceName = ReadString(item, ["SourceName", "Source"]) ?? sourceEnumLabel;
        bool? forComp = ReadBool(item, ["ForCompetition", "IsCompetition"]);
        int? val = ReadInt(item, ["Value", "TargetValue", "Parameter"]);
        if (val < 0) val = null;

        return new ClubVisionRow(
            typeCode,
            importance,
            start,
            end,
            itemName,
            itemIdx,
            sourceCode,
            sourceName,
            forComp,
            val,
            typeName);
    }

    static ClubAffiliationRow MapAffiliationRow(object item, IReadOnlyDictionary<int, string> clubsById)
    {
        var (parentName, parentId) = ReadClubRef(item, ["ParentClub", "Parent"], ["ParentClubId", "ParentId"], clubsById);
        var (feederName, feederId) = ReadClubRef(item, ["FeederClub", "Feeder", "AffiliatedClub"], ["FeederClubId", "FeederId", "AffiliatedClubId"], clubsById);
        var (typeCode, typeEnumLabel) = ReadCodeAndEnumLabel(item, ClubListLabels.AffiliationTypes, ["TypeCode", "Type", "Kind"]);
        string? typeName = ReadString(item, ["TypeName", "KindName"]) ?? typeEnumLabel;
        var start = ReadDate(item, ["StartDate", "Start", "Starts"]);
        var end = ReadDate(item, ["EndDate", "End", "Ends", "Expires"]);
        int? fee = ReadInt(item, ["AnnualFee", "Fee"]);
        uint? mask = ReadUInt(item, ["BenefitsMask", "Benefits"]);

        return new ClubAffiliationRow(
            parentName,
            feederName,
            typeCode,
            start,
            parentId,
            feederId,
            end,
            fee,
            mask,
            typeName);
    }

    static RegionalDivisionRow MapRegionalDivisionRow(object item)
    {
        byte level = ReadByte(item, ["Level"]) ?? 0;
        string? divName = ReadString(item, ["DivisionName", "CompetitionName", "Name"])
                          ?? ReadEntityName(item, ["Division", "Competition"]);
        int? compId = ReadInt(item, ["CompetitionId", "DivisionId", "Id"]);
        int? rawYear = ReadInt(item, ["Year"]);
        short? year = rawYear is { } y and >= short.MinValue and <= short.MaxValue ? (short)y : null;

        return new RegionalDivisionRow(level, divName, compId, year);
    }

    static ClubSponsorRow MapSponsorRow(object item)
    {
        var (typeCode, typeEnumLabel) = ReadCodeAndEnumLabel(item, ClubListLabels.SponsorIncomeTypes, ["IncomeTypeCode", "IncomeType", "TypeCode", "Type", "Kind"]);
        string? typeName = ReadString(item, ["IncomeTypeName", "TypeName", "KindName"]) ?? typeEnumLabel;
        int total = ReadInt(item, ["TotalValue", "Total", "Amount", "Value"]) ?? 0;
        int? annual = ReadInt(item, ["AnnualValue", "Annual", "PerYear", "SecondValue"]);
        var start = ReadDate(item, ["StartDate", "Start", "Starts"]);
        var end = ReadDate(item, ["EndDate", "End", "Ends", "Expires"]);

        return new ClubSponsorRow(typeCode, total, start, end, annual, typeName);
    }

    static ClubDebtRow MapDebtRow(object item)
    {
        var (sourceCode, sourceEnumLabel) = ReadCodeAndEnumLabel(item, ClubListLabels.DebtSources, ["SourceCode", "Source", "TypeCode", "Type", "Kind"]);
        string? sourceName = ReadString(item, ["SourceName", "TypeName", "KindName"]) ?? sourceEnumLabel;
        int debt = ReadInt(item, ["OriginalDebt", "Original", "Debt", "Amount"]) ?? 0;
        int? instalment = ReadInt(item, ["Instalment", "Installment", "Payment", "PeriodicPayment"]);
        var start = ReadDate(item, ["StartDate", "Start", "Starts"]);
        var end = ReadDate(item, ["EndDate", "End", "Ends", "Expires"]);

        return new ClubDebtRow(sourceCode, debt, start, end, instalment, sourceName);
    }

    static ClubKitRow MapKitRow(object item)
    {
        uint fg = ReadUInt(item, ["Foreground", "ForegroundColour"]) ?? 0;
        uint bg = ReadUInt(item, ["Background", "BackgroundColour"]) ?? 0;
        uint outline = ReadUInt(item, ["Outline", "OutlineColour"]) ?? 0;
        uint num = ReadUInt(item, ["NumberColour", "Number"]) ?? 0;
        uint numOut = ReadUInt(item, ["NumberOutline", "NumberOutlineColour"]) ?? 0;
        byte style = ReadByte(item, ["Style", "ItemStyle"]) ?? 0;

        var (kitCode, kitEnumLabel) = ReadOptionalCodeAndEnumLabel(item, ClubListLabels.KitKinds, ["KitKindCode", "KitKind", "KitNumber", "Kit", "Selector"]);
        string? kitName = ReadString(item, ["KitKindName", "KitName", "Kit"]) ?? kitEnumLabel;

        var (itemCode, itemEnumLabel) = ReadOptionalCodeAndEnumLabel(item, null, ["ItemTypeCode", "ItemType", "Item"]);
        string? itemName = ReadString(item, ["ItemTypeName", "ItemName", "Item"]) ?? itemEnumLabel;

        var (playerCode, playerEnumLabel) = ReadOptionalCodeAndEnumLabel(item, null, ["PlayerTypeCode", "PlayerType"]);
        string? playerName = ReadString(item, ["PlayerTypeName", "PlayerType"]) ?? playerEnumLabel;

        return new ClubKitRow(
            fg,
            bg,
            outline,
            num,
            numOut,
            style,
            kitCode,
            kitName,
            itemCode,
            itemName,
            playerCode,
            playerName);
    }

    static ClubTransferOfferRow MapTransferOfferRow(object item, IReadOnlyDictionary<int, string> clubsById)
    {
        int offerId = ReadInt(item, ["OfferId", "TransferId", "Id"]) ?? 0;
        string? personName = ReadString(item, ["PersonName", "PlayerName", "Person", "Name"])
                             ?? ReadEntityName(item, ["Person", "Player"]);
        int? personId = ReadInt(item, ["PersonId", "PlayerId"]);

        var (sellerName, sellerId) = ReadClubRef(item, ["SellerClub", "Seller", "FromClub", "From"], ["SellerClubId", "SellerId", "FromClubId"], clubsById);
        var (buyerName, buyerId) = ReadClubRef(item, ["BuyerClub", "Buyer", "ToClub", "To"], ["BuyerClubId", "BuyerId", "ToClubId"], clubsById);
        int? fee = ReadInt(item, ["Fee", "Amount"]);

        var (statusCode, statusEnumLabel) = ReadOptionalCodeAndEnumLabel(item, ClubListLabels.TransferOfferStatuses, ["StatusCode", "Status"]);
        string? statusName = ReadString(item, ["StatusName", "Status"]) ?? statusEnumLabel;

        return new ClubTransferOfferRow(
            offerId,
            personName,
            personId,
            sellerName,
            sellerId,
            buyerName,
            buyerId,
            fee,
            statusCode,
            statusName);
    }

    static (string? Name, int? Id) ReadClubRef(
        object item,
        ReadOnlySpan<string> entityProps,
        ReadOnlySpan<string> idProps,
        IReadOnlyDictionary<int, string> clubsById)
    {
        string? name = ReadString(item, entityProps);
        int? id = ReadInt(item, idProps);

        foreach (string propName in entityProps)
        {
            if (GetPropertyValue(item, propName) is not { } obj || obj is string) continue;
            name ??= ExtractName(obj);
            id ??= ReadInt(obj, ["Id"]);
        }

        if (string.IsNullOrWhiteSpace(name) && id is { } cid && clubsById.TryGetValue(cid, out var resolved))
            name = resolved;

        return (string.IsNullOrWhiteSpace(name) ? null : name, id);
    }

    static (byte Code, string? EnumLabel) ReadCodeAndEnumLabel(
        object item,
        IReadOnlyDictionary<byte, string>? oracleMap,
        ReadOnlySpan<string> names)
    {
        var (code, label) = ReadOptionalCodeAndEnumLabel(item, oracleMap, names);
        return (code ?? 0, label);
    }

    static (byte? Code, string? EnumLabel) ReadOptionalCodeAndEnumLabel(
        object item,
        IReadOnlyDictionary<byte, string>? oracleMap,
        ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is not { } val || val is string) continue;
            var t = val.GetType();
            if (t.IsEnum)
            {
                long raw = Convert.ToInt64(val, CultureInfo.InvariantCulture);
                byte code = (byte)Math.Clamp(raw, 0, 255);
                if (oracleMap is not null && oracleMap.ContainsKey(code))
                    return (code, null);
                string? enumText = Enum.IsDefined(t, val) ? ContractLabels.Spaced(val.ToString()!) : null;
                return (code, enumText);
            }
            if (TryToLong(val, out long num))
                return ((byte)Math.Clamp(num, 0, 255), null);
        }
        return (null, null);
    }

    static string? ReadString(object item, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    static string? ReadEntityName(object item, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is not { } val) continue;
            if (val is string s && !string.IsNullOrWhiteSpace(s)) return s;
            if (ExtractName(val) is { } extracted) return extracted;
        }
        return null;
    }

    static string? ExtractName(object entity) => entity switch
    {
        ClubInfo c => !string.IsNullOrWhiteSpace(c.Name) ? c.Name : !string.IsNullOrWhiteSpace(c.ShortName) ? c.ShortName : null,
        ClubEntry c => !string.IsNullOrWhiteSpace(c.Name) ? c.Name : !string.IsNullOrWhiteSpace(c.ShortName) ? c.ShortName : null,
        CompetitionInfo comp => !string.IsNullOrWhiteSpace(comp.Name) ? comp.Name : !string.IsNullOrWhiteSpace(comp.ShortName) ? comp.ShortName : null,
        PlayerRecord p => !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName : null,
        _ => ReadString(entity, ["Name", "FullName", "DisplayName", "ShortName"]),
    };

    static byte? ReadByte(object item, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is { } val && TryToLong(val, out long n))
                return (byte)Math.Clamp(n, 0, 255);
        }
        return null;
    }

    static int? ReadInt(object item, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is { } val && !val.GetType().IsEnum && TryToLong(val, out long n)
                && n is >= int.MinValue and <= int.MaxValue)
            {
                return (int)n;
            }
        }
        return null;
    }

    static uint? ReadUInt(object item, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is { } val && !val.GetType().IsEnum)
            {
                if (val is uint u) return u;
                if (val is int i) return unchecked((uint)i);
                if (TryToLong(val, out long n) && n is >= 0 and <= uint.MaxValue) return (uint)n;
            }
        }
        return null;
    }

    static bool? ReadBool(object item, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is bool b)
                return b;
        }
        return null;
    }

    static DateOnly? ReadDate(object item, ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (GetPropertyValue(item, name) is DateOnly d)
                return d.Year > 1900 ? d : new DateOnly(1900, 1, 1);
        }
        return null;
    }

    static bool TryToLong(object val, out long result)
    {
        switch (val)
        {
            case byte b: result = b; return true;
            case sbyte sb: result = sb; return true;
            case short s: result = s; return true;
            case ushort us: result = us; return true;
            case int i: result = i; return true;
            case uint ui: result = ui; return true;
            case long l: result = l; return true;
            default: result = 0; return false;
        }
    }

    static object? GetPropertyValue(object target, string propertyName)
    {
        var prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        return prop is not null && prop.GetIndexParameters().Length == 0 ? prop.GetValue(target) : null;
    }
}
