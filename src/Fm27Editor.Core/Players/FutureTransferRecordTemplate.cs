namespace Fm27Editor.Core.Players;

/// <summary>Experimental record bytes observed in FMRTE traces; does not read or write game memory.</summary>
public static class FutureTransferRecordTemplate
{
    public static byte[] Offer(ulong vtable, ulong person, ulong buyer, ulong seller, int fee, int id, int serial,
        ulong auxiliary, DateOnly today, DateOnly transferDate)
    {
        if (vtable == 0 || person == 0 || buyer == 0 || seller == 0 || auxiliary == 0)
            throw new ArgumentException("Missing offer object pointer");
        if (fee < 0 || id < 0 || serial < 0) throw new ArgumentOutOfRangeException(nameof(fee));
        var b = new byte[208];
        Put64(b, 0, vtable); b[8] = 1; b[0x31] = 0x10;
        Put64(b, 0x38, person); Put64(b, 0x40, buyer); Put64(b, 0x48, buyer); Put64(b, 0x50, seller);
        Put32(b, 0x58, fee); Put32(b, 0x5C, id); Put32(b, 0x60, serial);
        b.AsSpan(0x64, 8).Fill(0xFF);
        Date(b, 0x6C, new DateOnly(1900, 1, 1)); Date(b, 0x70, new DateOnly(1900, 1, 1));
        Date(b, 0x74, today); Date(b, 0x78, today.AddDays(30)); Date(b, 0x7C, transferDate);
        Date(b, 0x80, new DateOnly(1900, 1, 1));
        b[0x86] = 1; b[0x89] = 0xFF; b[0x8D] = 19; b[0x8E] = 1;
        Put64(b, 0xA8, auxiliary);
        return b;
    }

    public static byte[] ContractLink(ulong contract, int offerId)
    {
        if (contract == 0 || offerId < 0) throw new ArgumentException("Missing contract or offer ID");
        var b = new byte[72]; b[0] = 1; Put64(b, 0x10, contract); Put32(b, 0x28, offerId);
        new byte[] { 0x81, 0x69, 0x67, 0xFF }.CopyTo(b, 0x2C);
        Date(b, 0x38, new DateOnly(1900, 1, 1)); b[0x3E] = 2; b[0x3F] = 1; b[0x40] = 1;
        return b;
    }

    public static byte[] TransferDate(ulong person, DateOnly date, int offerId)
    {
        if (person == 0 || offerId < 0) throw new ArgumentException("Missing person or offer ID");
        var b = new byte[32]; Put64(b, 0, person); Date(b, 8, date); Put32(b, 0xC, offerId); return b;
    }

    static void Put32(byte[] b, int at, int value) => BitConverter.GetBytes(value).CopyTo(b, at);
    static void Put64(byte[] b, int at, ulong value) => BitConverter.GetBytes(value).CopyTo(b, at);
    static void Date(byte[] b, int at, DateOnly value)
    {
        BitConverter.GetBytes((short)value.DayOfYear).CopyTo(b, at);
        BitConverter.GetBytes((short)value.Year).CopyTo(b, at + 2);
    }
}
