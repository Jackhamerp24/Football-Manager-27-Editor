using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// A fake game built from the real FM27 profile. Raw offsets are written as literals taken from
/// the measured layout (see re/FM27-findings.md section 6), not looked up from the profile,
/// so a wrong profile entry makes the tests fail.
/// </summary>
sealed class TestWorld
{
    public const ulong ModuleBase = 0x7FF0_0000_0000;
    public const uint PlayerVtable = 0x4D27228;
    public const uint OtherVtable = 0x1234560;
    public const ulong Mainoo = 0x1000_0000, Yamal = 0x1000_1000, NotAPlayer = 0x1000_2000;
    public const ulong ListArray = 0x2000_0000;

    public static readonly string ProfilePath = Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2486695.json");
    public OffsetProfile Profile { get; } = OffsetProfile.LoadFile(ProfilePath);
    public FakeMemory Memory { get; } = new();

    public TestWorld()
    {
        var mainoo = Memory.Map(Mainoo, 0x3C8);
        Set64(mainoo, 0x000, ModuleBase + PlayerVtable);
        Set16(mainoo, 0x264, 153); // CA
        Set16(mainoo, 0x266, 177); // PA
        Set16(mainoo, 0x25E, 7382); Set16(mainoo, 0x260, 7551); Set16(mainoo, 0x262, 6501);
        Set16(mainoo, 0x22E, 184);  // height
        Set16(mainoo, 0x310, 109); Set16(mainoo, 0x312, 2005); // 19 April 2005 is day 109
        mainoo[0x15F] = 45;          // Crossing 9
        mainoo[0x185] = 66;          // Pace 13 (66 / 5 = 13.2)
        mainoo[0x194] = 70;          // Concentration 14
        mainoo[0x15C] = 20;          // Striker familiarity
        mainoo[0x2F9] = 10;          // Ambition
        Set64(mainoo, 0x2D8, 0x3000_0000); Memory.MapNameEntry(0x3000_0000, 0x3000_0100, "Kobbie");
        Set64(mainoo, 0x2E0, 0x3000_0200); Memory.MapNameEntry(0x3000_0200, 0x3000_0300, "Mainoo");
        Set64(mainoo, 0x2C8, 0x3000_0400); Memory.MapString(0x3000_0400, "Kobbie Boateng Mainoo");

        var yamal = Memory.Map(Yamal, 0x3C8);
        Set64(yamal, 0x000, ModuleBase + PlayerVtable);
        Set64(yamal, 0x2D8, 0x3000_0500); Memory.MapNameEntry(0x3000_0500, 0x3000_0600, "Lamine Yamal");
        Set64(yamal, 0x2E0, 0x3000_0700); Memory.MapNameEntry(0x3000_0700, 0x3000_0800, "Nasraoui");
        Set64(yamal, 0x2E8, 0x3000_0900); Memory.MapNameEntry(0x3000_0900, 0x3000_0A00, "Lamine Yamal");

        var other = Memory.Map(NotAPlayer, 0x3C8);
        Set64(other, 0x000, ModuleBase + OtherVtable);

        // Static root at game_plugin+0x57037C0: [+0x10] = first entry, [+0x18] = end.
        var root = Memory.Map(ModuleBase + 0x57037C0, 0x20);
        Set64(root, 0x10, ListArray);
        Set64(root, 0x18, ListArray + 4 * 8);
        var list = Memory.Map(ListArray, 4 * 8);
        Set64(list, 0, Mainoo); Set64(list, 8, NotAPlayer); Set64(list, 16, 0); Set64(list, 24, Yamal);
    }

    public static void Set64(byte[] d, int off, ulong v) => BitConverter.GetBytes(v).CopyTo(d, off);
    public static void Set16(byte[] d, int off, short v) => BitConverter.GetBytes(v).CopyTo(d, off);

    public byte[] Bytes(ulong address, int length) => Memory.Read(address, length);
}
