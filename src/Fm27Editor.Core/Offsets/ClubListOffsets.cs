using System.Text.Json;

namespace Fm27Editor.Core.Offsets;

/// <summary>Only list layouts with independent FMRTE oracles on Sheffield and Everton. All values are build-specific.</summary>
public sealed record ClubListOffsets(
    int Teams, int TeamDetails, int Vision, int VisionVector, uint VisionHolderVtable, uint CultureVtable,
    int CultureSize, int VisionType, int Importance, int VisionStart, int VisionEnd, int ItemIndex, int ItemMarker,
    int Affiliations, int AffiliationSize, int Parent, int Feeder, int AffiliationStart, int AffiliationType,
    int RegionalPointer, int RegionalSize, int Level)
{
    internal static ClubListOffsets Load(JsonElement e)
    {
        int H(string key) => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"club.lists.{key}: expected hex offset"), 16);
        return new(H("teams"), H("teamDetails"), H("vision"), H("visionVector"), (uint)H("visionHolderVtable"),
            (uint)H("cultureVtable"), H("cultureSize"), H("visionType"), H("importance"), H("visionStart"),
            H("visionEnd"), H("itemIndex"), H("itemMarker"), H("affiliations"), H("affiliationSize"),
            H("parent"), H("feeder"), H("affiliationStart"), H("affiliationType"), H("regionalPointer"), H("regionalSize"), H("level"));
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
    static readonly string[] Keys =
    [
        "sponsorsPointer", "sponsorSize", "sponsorStart", "sponsorEnd", "sponsorTotal", "sponsorType",
        "debtsPointer", "debtsVector", "debtSize", "debtOriginal", "debtEnd", "debtStart", "debtSource",
    ];

    internal static ClubFinanceListOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"club.financeLists.{key}: expected hex offset"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [SponsorsPointer, SponsorSize, SponsorStart, SponsorEnd, SponsorTotal, SponsorType,
                   DebtsPointer, DebtsVector, DebtSize, DebtOriginal, DebtEnd, DebtStart, DebtSource];
        w.WriteStartObject("financeLists");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}
