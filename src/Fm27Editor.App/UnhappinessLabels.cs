namespace Fm27Editor.App;

/// <summary>Names for unhappiness codes; only codes checked against a player in game are named, the rest show raw.</summary>
public static class UnhappinessLabels
{
    // Bruno Fernandes, Man Utd, 8/11/2026.
    static readonly Dictionary<short, string> Motives = new() { [600] = "Stripped of the captaincy" };
    static readonly Dictionary<byte, string> Types = new() { [1] = "Concerned" };

    public static string Motive(short code) => Motives.TryGetValue(code, out var s) ? s : $"Motive {code}";
    public static string Type(byte code) => Types.TryGetValue(code, out var s) ? s : $"Type {code}";
}
