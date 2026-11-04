namespace Fm27Editor.App;

/// <summary>
/// FMRTE 27's Information dropdowns, in its order, with the codes the game stores. Codes come from FMRTE 26's enums and
/// were confirmed by FMRTE 27 writes on Bruno Fernandes (Endomorph 5, Red 2 13, Long 3, Skin Tone 15, Pacific Islander 6,
/// Has Played For Nation 81) and by Harry Kane's values (Mesomorph 3, Light Brown 2 9, Short 1, Skin Tone 4,
/// Northern European 0, Declared For Nation 83).
/// </summary>
public static class InformationLabels
{
    public static readonly IReadOnlyList<(int Code, string Name)> BodyTypes =
        [(1, "Ectomorph"), (2, "Ecto-Mesomorph"), (3, "Mesomorph"), (4, "Meso-Endomorph"), (5, "Endomorph")];

    public static readonly IReadOnlyList<(int Code, string Name)> HairColours =
    [
        (5, "Black 1"), (15, "Black 2"), (16, "Black 3"), (1, "Blonde 1"), (7, "Blonde 2"), (8, "Blonde 3"),
        (3, "Dark Brown 1"), (11, "Dark Brown 2"), (12, "Dark Brown 3"), (6, "Grey 1"), (17, "Grey 2"), (18, "Grey 3"),
        (2, "Light Brown 1"), (9, "Light Brown 2"), (10, "Light Brown 3"), (4, "Red 1"), (13, "Red 2"), (14, "Red 3"), (0, "Unknown"),
    ];

    public static readonly IReadOnlyList<(int Code, string Name)> HairLengths =
        [(4, "Bald"), (0, "Fuzz"), (3, "Long"), (2, "Medium"), (1, "Short"), (5, "Unknown")];

    public static readonly IReadOnlyList<(int Code, string Name)> SkinColours =
        [(0, "Unknown"), .. Enumerable.Range(1, 19).Select(i => (i, $"Skin Tone {i}"))];

    public static readonly IReadOnlyList<(int Code, string Name)> Ethnicities =
    [
        (3, "African/Caribbean"), (4, "Asian"), (10, "East Asian"), (1, "Mediterranean/Hispanic"), (9, "Mixed Race (Black/White)"),
        (7, "Native American"), (8, "Native Australian"), (2, "North African/Middle Eastern"), (0, "Northern European"),
        (6, "Pacific Islander"), (5, "South East Asian"),
    ];

    public static readonly IReadOnlyList<(int Code, string Name)> NationalityInfos =
    [
        (0, "No info"), (80, "Eligible For Nation"), (81, "Has Played For Nation"), (82, "Gained Citizenship But Treated As Foreign"),
        (83, "Declared For Nation"), (84, "Relative Born In Nation"), (85, "Born In Nation"), (86, "Not Eligible For Nation"),
        (87, "Gained Citizenship Through Relative"), (88, "Gained Citizenship But Not Eligible For Nation Yet"),
        (89, "Gained Citizenship And Declared For Nation"), (90, "Gained Citizenship Through Relative But Not Eligible For Nation Yet"),
    ];

    public static string Name(IReadOnlyList<(int Code, string Name)> list, int code) =>
        list.FirstOrDefault(x => x.Code == code).Name ?? $"Code {code}";
}
