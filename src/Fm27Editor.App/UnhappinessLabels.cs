namespace Fm27Editor.App;

/// <summary>Names for unhappiness codes: motives from <see cref="UnhappinessMotives"/>, types in FMRTE's order.</summary>
public static class UnhappinessLabels
{
    /// <summary>Type labels by code (1-7), FMRTE 27's dropdown in its order: <see cref="Core.Calibration.FmrteLabels.UnhappinessType"/>.</summary>
    public static IEnumerable<byte> TypeCodes => Enumerable.Range(1, Core.Calibration.FmrteLabels.UnhappinessType.Length).Select(c => (byte)c);

    public static string Motive(short code) => UnhappinessMotives.All.TryGetValue(code, out var m) ? m.Name : $"Motive {code}";
    public static bool NeedsPerson(short code) => UnhappinessMotives.All.TryGetValue(code, out var m) && m.NeedsPerson;
    public static string Type(byte code) => Core.Calibration.FmrteLabels.UnhappinessTypeOf(code);
}
