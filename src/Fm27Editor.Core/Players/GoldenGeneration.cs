using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>What Golden Generation gives one player.</summary>
public readonly record struct GoldenGenerationValues(int PotentialAbility, int CurrentAbility, int Professionalism, int Ambition);

/// <summary>One Golden Generation roll: the player's four values before it, and the four to write.</summary>
public sealed record GoldenGenerationPlan(PlayerRecord Player, GoldenGenerationValues Before, GoldenGenerationValues After);

/// <summary>
/// Golden Generation, as FM Studio 1.1.1 does it (CIL of ClubYouthService.ApplyGoldenGeneration, 8/11/2026): PA is a random
/// value in [min, max] (both kept to 1–200; a reversed pair is refused; the studio's defaults are 150 and 180); CA is random in
/// [min(90, hi), hi] with hi = min(125, PA − 20), then kept to 1..PA; Professionalism 19–20 and Ambition 17–19.
/// Only those four fields are written. The plan reads them, applying checks all four before writing any, and Undo puts the
/// snapshot back. <see cref="Random"/> may be seeded so a roll can be replayed.
/// </summary>
public sealed class GoldenGenerationWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, Random? random = null)
{
    public const int DefaultMinPa = 150, DefaultMaxPa = 180;
    readonly Random _random = random ?? Random.Shared;

    /// <summary>Both bounds are kept to 1–200; a reversed pair is refused, the same rule as the detailed windows.</summary>
    public static (int Min, int Max) NormalisePotential(int min, int max)
    {
        var window = GoldenGenerationPotential.Custom(min, max);
        return (window.Min, window.Max);
    }

    /// <summary>The four values a roll produces, without touching the game.</summary>
    public GoldenGenerationValues Roll(int minPa, int maxPa)
    {
        var (min, max) = NormalisePotential(minPa, maxPa);
        int pa = _random.Next(min, max + 1);
        int hi = Math.Min(125, pa - 20), lo = Math.Min(90, hi);
        int ca = Math.Clamp(_random.Next(lo, hi + 1), 1, pa);
        return new(pa, ca, _random.Next(19, 21), _random.Next(17, 20));
    }

    /// <summary>Reads the player's four values and rolls replacements; throws when the player object is not the one read.</summary>
    public GoldenGenerationPlan Plan(PlayerRecord player, int minPa = DefaultMinPa, int maxPa = DefaultMaxPa)
    {
        RequirePlayer(player);
        var before = Read(player);
        // The record must still describe the game object: a caller holding a stale snapshot is refused before any write.
        if (player.PotentialAbility is { } pa && pa != before.PotentialAbility
            || player.CurrentAbility is { } ca && ca != before.CurrentAbility
            || player.Personality[(int)PersonalityAttribute.Professionalism] != before.Professionalism
            || player.Personality[(int)PersonalityAttribute.Ambition] != before.Ambition)
            throw new StaleObjectException($"{player.DisplayName}: the player record is older than the game object");
        return new(player, before, Roll(minPa, maxPa));
    }

    /// <summary>Plans, applies and returns the values written.</summary>
    public GoldenGenerationValues Apply(PlayerRecord player, int minPa = DefaultMinPa, int maxPa = DefaultMaxPa)
    {
        var plan = Plan(player, minPa, maxPa);
        Apply(plan);
        return plan.After;
    }

    /// <summary>Writes PA, CA, Professionalism and Ambition only, after all four still hold the planned "before" values.</summary>
    public void Apply(GoldenGenerationPlan plan)
    {
        Guard(plan, plan.Before);
        Write(plan.Player, plan.After);
    }

    /// <summary>Puts the plan's snapshot back, only while the player still holds the values it wrote.</summary>
    public void Undo(GoldenGenerationPlan plan)
    {
        Guard(plan, plan.After);
        Write(plan.Player, plan.Before);
    }

    GoldenGenerationValues Read(PlayerRecord player)
    {
        var fields = profile.Fields;
        return new(
            BitConverter.ToInt16(memory.Read(player.Address + (ulong)fields.PotentialAbility, 2)),
            BitConverter.ToInt16(memory.Read(player.Address + (ulong)fields.CurrentAbility, 2)),
            memory.Read(player.Address + (ulong)profile.Personality[PersonalityAttribute.Professionalism], 1)[0],
            memory.Read(player.Address + (ulong)profile.Personality[PersonalityAttribute.Ambition], 1)[0]);
    }

    void Guard(GoldenGenerationPlan plan, GoldenGenerationValues expected)
    {
        RequirePlayer(plan.Player);
        var current = Read(plan.Player);
        if (current != expected)
            throw new StaleObjectException($"{plan.Player.DisplayName}: ability or personality changed since the plan was made");
    }

    void RequirePlayer(PlayerRecord player)
    {
        if (player.Id is not > 0 || profile.Fields.Id is not { } id
            || !profile.PlayerVtableRvas.Any(rva => memory.ReadUInt64(player.Address) == moduleBase + rva)
            || BitConverter.ToInt32(memory.Read(player.Address + (ulong)id, 4)) != player.Id)
            throw new StaleObjectException($"0x{player.Address:X} is no longer this player object");
    }

    void Write(PlayerRecord player, GoldenGenerationValues values)
    {
        var fields = profile.Fields;
        Write(player, fields.PotentialAbility, BitConverter.GetBytes((short)values.PotentialAbility));
        Write(player, fields.CurrentAbility, BitConverter.GetBytes((short)values.CurrentAbility));
        Write(player, profile.Personality[PersonalityAttribute.Professionalism], [(byte)values.Professionalism]);
        Write(player, profile.Personality[PersonalityAttribute.Ambition], [(byte)values.Ambition]);
    }

    void Write(PlayerRecord player, int offset, byte[] value)
    {
        ulong at = player.Address + (ulong)offset;
        if (!memory.TryWrite(at, value) || !memory.Read(at, value.Length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write 0x{at:X}");
    }
}
