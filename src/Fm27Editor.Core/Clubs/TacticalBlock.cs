using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Clubs;

/// <summary>The Tactical Understanding sliders of one team, 0-100 as FMRTE shows them. Position/Role stays null until
/// its offset is measured (the +0x120 slot holds pointers, so it is not that slider).</summary>
public sealed record TacticalValues(int BuildUp, int ChanceCreation, int PassingStyle, int PressingTrap,
    int LineOfEngagement, int PressingIntensity, int? PositionRole = null);

/// <summary>
/// Reads a team's Tactical Understanding block. Each slider is a group of consecutive dwords 0x10 apart starting at
/// +0xC0; bits 0-13 hold value × 100 and bits 14+ hold per-dword flags (measured on build 2493426 by moving each slider
/// in FMRTE 27 and diffing RAM). Position/Role is not mapped yet.
/// </summary>
public static class TacticalBlock
{
    public const int BuildUp = 0xC0, ChanceCreation = 0xD0, PassingStyle = 0xE0, PressingTrap = 0xF0,
        LineOfEngagement = 0x100, PressingIntensity = 0x110;
    public const int ValueMask = 0x3FFF;
    public const int Size = 0x120;

    /// <summary>Null when the block cannot be read or one of the six mapped sliders is outside 0-100.</summary>
    public static TacticalValues? Read(IProcessMemory memory, ulong block)
    {
        if (block == 0) return null;
        var bytes = new byte[Size];
        if (!memory.TryRead(block, bytes)) return null;
        int? Value(int offset)
        {
            uint raw = BitConverter.ToUInt32(bytes, offset);
            int scaled = (int)(raw & ValueMask);
            return scaled <= 10_000 ? scaled / 100 : null;
        }
        if (Value(BuildUp) is not { } buildUp || Value(ChanceCreation) is not { } chance || Value(PassingStyle) is not { } passing
            || Value(PressingTrap) is not { } trap || Value(LineOfEngagement) is not { } line || Value(PressingIntensity) is not { } press)
            return null;
        return new(buildUp, chance, passing, trap, line, press);
    }
}
