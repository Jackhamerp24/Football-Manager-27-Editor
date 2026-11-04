using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Awards;

/// <summary>An award from the game-wide list (263 on 3/11/2026), as FMRTE 27's Award page shows it.</summary>
public sealed record AwardRecord(ulong Address, int Index, int Id, string? Name, string? ShortName, short Reputation,
    byte WinnerHomeReputation, byte WinnerWorldReputation)
{
    public string DisplayName => Name ?? ShortName ?? "";
}

public sealed class AwardRepository(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxAwards = 10_000, MaxName = 300;
    AwardOffsets A => profile.Award ?? throw new InvalidOperationException("Profile has no award offsets");

    public IReadOnlyList<AwardRecord> LoadAll()
    {
        var a = A;
        ulong root = memory.ReadUInt64(moduleBase + (ulong)a.ListRootRva);
        ulong begin = root == 0 ? 0 : memory.ReadUInt64(root + (ulong)a.ListOffset), end = root == 0 ? 0 : memory.ReadUInt64(root + (ulong)a.ListOffset + 8);
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxAwards)
            throw new MemoryAccessException($"Award list looks invalid (begin 0x{begin:X}, end 0x{end:X}); is a save loaded?");
        var raw = memory.Read(begin, (int)(end - begin));
        var result = new List<AwardRecord>(raw.Length / 8);
        for (int i = 0; i < raw.Length; i += 8)
            if (Reload(BitConverter.ToUInt64(raw, i)) is { } r) result.Add(r);
        return result;
    }

    public AwardRecord? Reload(ulong address)
    {
        var a = A;
        var b = new byte[new[] { a.Name, a.ShortName, a.Index, a.Id, a.Reputation, a.WinnerHomeReputation, a.WinnerWorldReputation }.Max() + 8];
        if (address == 0 || !memory.TryRead(address, b) || BitConverter.ToUInt64(b) != moduleBase + a.VtableRva) return null;
        return new(address, BitConverter.ToInt32(b, a.Index), BitConverter.ToInt32(b, a.Id), StringAt(BitConverter.ToUInt64(b, a.Name)),
            StringAt(BitConverter.ToUInt64(b, a.ShortName)), BitConverter.ToInt16(b, a.Reputation), b[a.WinnerHomeReputation], b[a.WinnerWorldReputation]);
    }

    string? StringAt(ulong p)
    {
        if (p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n < 0 || n > MaxName) return null;
        var bytes = new byte[n];
        return memory.TryRead(p + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}

public enum AwardField { Reputation, WinnerHomeReputation, WinnerWorldReputation }

/// <summary>
/// Edits an award's numbers. FMRTE 27 limits (probed on Sky Bet Championship Player of the Season, 3/11/2026): Reputation 14 → 20, 21
/// and 30 saved, 40, 50, 100, 200 and 201 refused (31–39 not probed, so 30 is the editor's limit); Winner home reputation 255 saved,
/// 256 refused; Winner world reputation is the next byte (33 and 70 saved, only that byte changed). Guarded: award vtable, field
/// still equal to the snapshot, read back.
/// </summary>
public sealed class AwardWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int MaxReputation = 30, MaxWinnerReputation = 255;

    public void Set(AwardRecord award, AwardField field, int value)
    {
        var a = profile.Award ?? throw new InvalidOperationException("Profile has no award offsets");
        int max = field == AwardField.Reputation ? MaxReputation : MaxWinnerReputation;
        if (value < 0 || value > max) throw new ArgumentOutOfRangeException(nameof(value), $"{field} is 0–{max}");
        if (memory.ReadUInt64(award.Address) != moduleBase + a.VtableRva) throw new StaleObjectException("The award object changed since it was read");
        (int at, byte[] before, byte[] after) = field switch
        {
            AwardField.Reputation => (a.Reputation, BitConverter.GetBytes(award.Reputation), BitConverter.GetBytes((short)value)),
            AwardField.WinnerHomeReputation => (a.WinnerHomeReputation, new[] { award.WinnerHomeReputation }, new[] { (byte)value }),
            _ => (a.WinnerWorldReputation, new[] { award.WinnerWorldReputation }, new[] { (byte)value }),
        };
        ulong address = award.Address + (ulong)at;
        if (!memory.Read(address, before.Length).AsSpan().SequenceEqual(before)) throw new StaleObjectException($"{field} changed since it was read");
        if (!memory.TryWrite(address, after) || !memory.Read(address, after.Length).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write 0x{address:X}");
    }
}
