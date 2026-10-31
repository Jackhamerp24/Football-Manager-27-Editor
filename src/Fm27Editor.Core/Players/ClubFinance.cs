using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// The money a club screen shows. Every field is optional because the offsets were measured on one
/// club only; a field the profile does not declare stays null instead of being guessed.
/// </summary>
public sealed record ClubFinance(ulong Address, int? Balance, int? TransferBudget, int? SpentThisSeason, int? WageSpend);

/// <summary>Club → finance object (club+0x150 on 27.0.0+2486695) and its int32 totals.</summary>
public sealed class ClubFinanceReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxRead = 0x1000;

    public ClubFinance? Read(ulong clubAddress)
    {
        if (clubAddress == 0 || profile.Club?.Finance is not { } f) return null;
        Span<byte> vt = stackalloc byte[8];
        if (!memory.TryRead(clubAddress, vt) || BitConverter.ToUInt64(vt) != moduleBase + profile.Club.ClubVtableRva) return null;
        ulong finance = memory.ReadUInt64(clubAddress + (ulong)f.Pointer);
        if (finance == 0) return null;
        uint fvt = (uint)(memory.ReadUInt64(finance) - moduleBase);
        return new ClubFinance(
            finance,
            Int32(finance, f, fvt, f.Balance),
            Int32(finance, f, fvt, f.TransferBudget),
            Int32(finance, f, fvt, f.SpentThisSeason),
            Int32(finance, f, fvt, f.WageSpend));
    }

    int? Int32(ulong finance, ClubFinanceOffsets f, uint vt, int? offset)
    {
        if (offset is not { } off || off < 0 || off + 4 > MaxRead || f.Holds(vt, off, 4) != true) return null;
        Span<byte> b = stackalloc byte[4];
        return memory.TryRead(finance + (ulong)off, b) ? BitConverter.ToInt32(b) : null;
    }
}
