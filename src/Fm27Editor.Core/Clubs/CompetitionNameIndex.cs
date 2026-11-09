using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Clubs;

/// <summary>
/// Competition id → name index built by walking the heap instead of the game's competition list.
/// On <c>27.0.0+2493426</c> (Game Pass) the profile's list root points at a different class, so the list reader returns
/// nothing; the competition objects themselves are still there: vtable at +0x00, id at +0xC, name pointer at +0x40
/// (count-prefixed UTF-8), verified against a live object (id 1 → "Jupiler Pro League").
/// </summary>
public sealed class CompetitionNameIndex(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int IdOffset = 0xC, NameOffset = 0x40;
    const int MaxName = 200;

    Dictionary<int, string>? cached;

    public IReadOnlyDictionary<int, string> All => cached ??= Scan();

    /// <summary>Name of one competition object, or null when this address is not a competition with a sane name.</summary>
    public string? TryRead(ulong address)
    {
        if (profile.Competition?.VtableRva is not { } vtableRva) return null;
        Span<byte> head = stackalloc byte[8];
        if (!memory.TryRead(address, head) || BitConverter.ToUInt64(head) != moduleBase + vtableRva) return null;
        var body = memory.Read(address, NameOffset + 8);
        int id = BitConverter.ToInt32(body, IdOffset);
        if (id is < 1 or > 100_000) return null;
        return Text(BitConverter.ToUInt64(body, NameOffset));
    }

    Dictionary<int, string> Scan()
    {
        var found = new Dictionary<int, string>();
        if (memory is not WindowsProcessMemory windows || profile.Competition?.VtableRva is not { } vtableRva) return found;
        ulong wanted = moduleBase + vtableRva;
        var buffer = new byte[1 << 22];
        foreach (var region in windows.EnumerateWritableRegions())
        {
            if (region.IsModuleData) continue;
            for (ulong offset = 0; offset + 8 <= region.Size; offset += (ulong)buffer.Length)
            {
                int length = (int)Math.Min((ulong)buffer.Length, region.Size - offset);
                if (!memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
                for (int i = 0; i + 8 <= length; i += 8)
                {
                    if (BitConverter.ToUInt64(buffer, i) != wanted) continue;
                    ulong address = region.Start + offset + (ulong)i;
                    if (TryRead(address) is not { } name) continue;
                    int id = BitConverter.ToInt32(memory.Read(address + IdOffset, 4), 0);
                    found.TryAdd(id, name);
                }
            }
        }
        return found;
    }

    string? Text(ulong pointer)
    {
        if (pointer < 0x10000) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(pointer, len)) return null;
        int count = BitConverter.ToInt32(len);
        if (count is < 2 or > MaxName) return null;
        var bytes = new byte[count];
        if (!memory.TryRead(pointer + 4, bytes)) return null;
        var text = Encoding.UTF8.GetString(bytes);
        return text.All(ch => ch >= ' ' && ch != '\uFFFD') ? text : null;
    }
}
