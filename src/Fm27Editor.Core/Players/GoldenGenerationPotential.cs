namespace Fm27Editor.Core.Players;

/// <summary>The potential-ability window one roll picks from: both ends kept to 1–200, minimum not above maximum.</summary>
public readonly record struct GoldenGenerationPotential(int Min, int Max)
{
    public string Display => $"{Min}–{Max}";

    /// <summary>
    /// A band typed by hand. Both ends are kept to 1–200 first, and a window whose ends cross is refused rather than
    /// swapped, so a typo stays visible instead of silently rolling somewhere else.
    /// </summary>
    public static GoldenGenerationPotential Custom(int min, int max)
    {
        min = Math.Clamp(min, 1, 200);
        max = Math.Clamp(max, 1, 200);
        if (min > max) throw new ArgumentException($"Potential {min}–{max} has its minimum above its maximum");
        return new(min, max);
    }

    /// <summary>One uniform value inside the window; both ends are reachable.</summary>
    public int Roll(Random random) => random.Next(Min, Max + 1);
}

/// <summary>
/// The preset bands: width 30, stepping by 10, from 10–40 up to 170–200. Seventeen of them, and neighbours overlap by 20,
/// so a player can be planned from two neighbouring presets and still get a value inside both.
/// </summary>
public static class GoldenGenerationBands
{
    public const int Count = 17, Width = 30, Step = 10, LowestMin = 10;

    static readonly GoldenGenerationPotential[] Bands =
        Enumerable.Range(0, Count)
            .Select(i => new GoldenGenerationPotential(LowestMin + i * Step, LowestMin + i * Step + Width))
            .ToArray();

    public static IReadOnlyList<GoldenGenerationPotential> All => Bands;

    public static GoldenGenerationPotential Band(int index) =>
        index is >= 0 and < Count ? Bands[index] : throw new ArgumentOutOfRangeException(nameof(index), index, $"Band must be 0–{Count - 1}");
}

/// <summary>Chance that a roll lifts the player towards its potential; the six offered values, plus Off.</summary>
public enum WonderkidChance { Off = 0, Five = 5, Ten = 10, TwentyFive = 25, Fifty = 50, SeventyFive = 75, NinetyNine = 99 }

/// <summary>The potential half of a roll: the window, or — one chance in 64 when asked for — the window's top value.</summary>
public static class GoldenGenerationRoll
{
    public const int MaxPotentialDivisor = 64;

    public static int Potential(GoldenGenerationPotential window, bool maxPotentialChance, Random random) =>
        maxPotentialChance && random.Next(MaxPotentialDivisor) == 0 ? window.Max : window.Roll(random);
}

/// <summary>
/// The wonderkid half of the roll. A hit closes a share of the gap between the rolled potential and the current ability:
/// the share depends on the potential (30/40/55/70), the chance of hitting at all is the offered percentage.
/// </summary>
public static class WonderkidRoll
{
    /// <summary>The chances the feature offers, lowest first.</summary>
    public static readonly WonderkidChance[] Offered = [WonderkidChance.Five, WonderkidChance.Ten, WonderkidChance.TwentyFive,
        WonderkidChance.Fifty, WonderkidChance.SeventyFive, WonderkidChance.NinetyNine];

    public static bool IsOffered(WonderkidChance chance) => chance == WonderkidChance.Off || Array.IndexOf(Offered, chance) >= 0;

    /// <summary>Share of the gap a hit closes: potential below 120 → 30, below 150 → 40, below 180 → 55, then 70.</summary>
    public static int GapPercent(int potential) => potential switch { < 120 => 30, < 150 => 40, < 180 => 55, _ => 70 };

    /// <summary>Room to grow: below the potential and below 70% of it. The comparison is exact, not floating point.</summary>
    public static bool Eligible(int potential, int current) => current < potential && current * 100 < potential * 70;

    /// <summary>The lift a hit gives: the gap at the band's share, floored. The caller caps the result at the potential.</summary>
    public static int Gain(int potential, int current) => (potential - current) * GapPercent(potential) / 100;

    /// <summary>The ability after the chance: unchanged on Off, without room to grow, or on a miss.</summary>
    public static int CurrentAfter(int potential, int current, WonderkidChance chance, Random random)
    {
        if (chance == WonderkidChance.Off || !Eligible(potential, current)) return current;
        if (random.Next(100) >= (int)chance) return current;
        return Math.Min(potential, current + Gain(potential, current));
    }
}
