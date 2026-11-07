using System.Text;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Offsets;

/// <summary>
/// A copy of the mapped module image taken from the running game, indexed by RVA. Unreadable pages stay zero so
/// callers can scan the whole image without special cases.
/// </summary>
public sealed class ModuleImage
{
    public ulong ModuleBase { get; }
    public byte[] Bytes { get; }

    ModuleImage(ulong moduleBase, byte[] bytes) => (ModuleBase, Bytes) = (moduleBase, bytes);

    public static ModuleImage Read(IProcessMemory memory, ulong moduleBase, int size)
    {
        if (size <= 0 || size > 512 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var bytes = new byte[size];
        const int chunk = 1 << 16;
        for (int offset = 0; offset < size; offset += chunk)
            memory.TryRead(moduleBase + (ulong)offset, bytes.AsSpan(offset, Math.Min(chunk, size - offset)));
        return new ModuleImage(moduleBase, bytes);
    }

    public uint U32(int offset) => BitConverter.ToUInt32(Bytes, offset);
    public ulong U64(int offset) => BitConverter.ToUInt64(Bytes, offset);
    public bool InImage(ulong address) => address >= ModuleBase && address - ModuleBase < (ulong)Bytes.Length;
    public int RvaOf(ulong address) => checked((int)(address - ModuleBase));
}

/// <summary>
/// MSVC RTTI index of the module: class name to vtable RVA. FMRTE-independent; used to re-find every vtable after a
/// game update instead of trusting an address recorded for an older build.
/// </summary>
public sealed class RttiIndex
{
    readonly Dictionary<string, List<uint>> byName;
    readonly Dictionary<uint, string> nameByVtable;

    RttiIndex(Dictionary<string, List<uint>> byName, Dictionary<uint, string> nameByVtable)
        => (this.byName, this.nameByVtable) = (byName, nameByVtable);

    public IReadOnlyDictionary<string, List<uint>> ByName => byName;

    /// <summary>Class name of one vtable RVA, or null when the slot does not carry a decodable MSVC RTTI locator.</summary>
    public string? NameOf(uint vtableRva) => nameByVtable.GetValueOrDefault(vtableRva);

    public IReadOnlyList<uint> Vtables(string className) => byName.GetValueOrDefault(className) ?? [];

    public static RttiIndex Build(ModuleImage image)
    {
        var bytes = image.Bytes;
        // 1. Type descriptors: ".\0?AV<name>\0" with a 16-byte header in front.
        var descriptors = new Dictionary<uint, string>();
        for (int i = 16; i + 8 < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'.' || bytes[i + 1] != (byte)'?' || bytes[i + 2] != (byte)'A' ||
                (bytes[i + 3] != (byte)'V' && bytes[i + 3] != (byte)'U')) continue;
            int length = 4;
            while (length < 250 && bytes[i + length] != 0) length++;
            if (length >= 250) continue;
            descriptors.TryAdd((uint)(i - 16), Encoding.ASCII.GetString(bytes, i, length));
            i += length;
        }
        // 2. Complete object locators: { signature 1, offset, cdOffset, typeDescriptor, classDescriptor, self }.
        var locators = new Dictionary<uint, string>();
        for (int p = 12; p + 24 <= bytes.Length; p += 4)
        {
            if (!descriptors.TryGetValue(BitConverter.ToUInt32(bytes, p), out string? name)) continue;
            int col = p - 12;
            if (BitConverter.ToUInt32(bytes, col) != 1) continue;
            if (BitConverter.ToUInt32(bytes, col + 0x14) != (uint)col) continue;
            locators[(uint)col] = name;
        }
        // 3. Vtables: the slot right before the first virtual method points at the locator.
        var byName = new Dictionary<string, List<uint>>();
        var nameByVtable = new Dictionary<uint, string>();
        for (int q = 0; q + 8 <= bytes.Length; q += 8)
        {
            ulong value = BitConverter.ToUInt64(bytes, q);
            if (value < image.ModuleBase) continue;
            ulong rva = value - image.ModuleBase;
            if (rva > uint.MaxValue || !locators.TryGetValue((uint)rva, out string? name)) continue;
            uint vtable = (uint)(q + 8);
            if (!byName.TryGetValue(name, out var list)) byName[name] = list = [];
            list.Add(vtable);
            nameByVtable[vtable] = name;
        }
        return new RttiIndex(byName, nameByVtable);
    }
}
