namespace Fm27Editor.App;

/// <summary>
/// FMRTE 27's Unavailability dropdowns with the codes FMRTE wrote for each entry (Kobbie Mainoo, 2/11/2026). Scope 10 is
/// from game bans FMRTE showed as "All nation competitions" (Oxlade-Chamberlain, Boateng). Scopes marked with an item
/// also store a competition, nation or continent number in the record's Item field.
/// </summary>
public static class BanLabels
{
    public sealed record Scope(int Code, string Name, bool HasItem);

    public static readonly IReadOnlyList<Scope> Scopes =
    [
        new(1, "Competition", true),
        new(5, "Domestic Cup", true),
        new(6, "Domestic League", true),
        new(10, "All nation competitions", true),
        new(15, "All continent competitions", true),
        new(20, "All international matches", false),
        new(25, "Global Ban", false),
        new(26, "Unavailable to play club football", false),
        new(28, "Suffering from undisclosed injury", false),
        new(29, "Cannot play football due to medical reasons", false),
        new(30, "Cannot play football due to personal reasons", false),
        new(31, "Holidays", false),
        new(34, "Isn't going to be registered.", false),
        new(38, "Cannot play club football", false),
        new(39, "Cannot play club football in official matches", false),
    ];

    /// <summary>Reason codes in dropdown order; FMRTE wrote 2 for Sending off, 4 for Misconduct and −1 for None, the rest follow the order.</summary>
    public static readonly IReadOnlyList<(int Code, string Name)> Reasons =
    [
        (0, "Received X yellow cards"), (1, "X points"), (2, "Sending off"), (3, "3 bookings in last 10 matches"),
        (4, "Misconduct"), (5, "X bookings in last 3 months"), (-1, "None"),
    ];

    public static string ScopeName(int code) => Scopes.FirstOrDefault(s => s.Code == code)?.Name ?? $"Code {code}";

    public static string ReasonName(int code) => Reasons.FirstOrDefault(r => r.Code == code).Name ?? $"Code {code}";
}
