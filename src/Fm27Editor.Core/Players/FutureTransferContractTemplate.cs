namespace Fm27Editor.Core.Players;

/// <summary>
/// Candidate 200-byte contract matching the three FMRTE 27 future-transfer traces. This does not write to a game;
/// the unexplained defaults and expiry rule must be verified before connecting it to a writer.
/// </summary>
public static class FutureTransferContractTemplate
{
    public static byte[] Build(ReadOnlySpan<byte> original, ulong destinationTeam, DateOnly gameDate,
        DateOnly transferDate, ulong terms, int termsLength, ulong extraTerms, int extraTermsLength)
    {
        if (original.Length != 200) throw new ArgumentException("Expected a 200-byte contract", nameof(original));
        if (destinationTeam == 0) throw new ArgumentOutOfRangeException(nameof(destinationTeam));
        if (termsLength < 0 || termsLength % 8 != 0 || (terms == 0) != (termsLength == 0))
            throw new ArgumentException("Invalid contract terms vector");
        if (extraTermsLength < 0 || extraTermsLength % 32 != 0 || (extraTerms == 0) != (extraTermsLength == 0))
            throw new ArgumentException("Invalid additional terms vector");
        var b = original.ToArray();
        Put64(b, 0x10, destinationTeam);
        Put64(b, 0x18, destinationTeam);
        b.AsSpan(0x24, 2).Clear();
        b.AsSpan(0x28, 0x14).Clear();
        b[0x2D] = 0x20;
        b.AsSpan(0x3C, 8).Fill(0xFF);
        Date(b, 0x44, transferDate);
        Date(b, 0x48, new DateOnly(checked(transferDate.Year + 3), 6, 30));
        Date(b, 0x4C, new DateOnly(1900, 1, 1));
        Date(b, 0x50, new DateOnly(1900, 1, 1));
        b[0x55] = 0; b[0x56] = 1; b[0x57] = 0;
        new byte[] { 0, 0, 0x5E, 0x5C, 0x14, 0xFF, 0, 0 }.CopyTo(b, 0x58);
        b.AsSpan(0x60, 8).Clear();
        Vector(b, 0x68, terms, termsLength);
        Vector(b, 0x80, extraTerms, extraTermsLength);
        b.AsSpan(0x98, 16).Clear();
        b.AsSpan(0xB0, 4).Clear();
        Date(b, 0xB4, gameDate);
        b.AsSpan(0xB8, 4).Clear();
        BitConverter.GetBytes(transferDate.Year).CopyTo(b, 0xBC);
        return b;
    }

    static void Vector(byte[] b, int at, ulong start, int length)
    {
        Put64(b, at, start);
        ulong end = checked(start + (ulong)length);
        Put64(b, at + 8, end);
        Put64(b, at + 16, end);
    }

    static void Put64(byte[] b, int at, ulong value) => BitConverter.GetBytes(value).CopyTo(b, at);
    static void Date(byte[] b, int at, DateOnly date)
    {
        BitConverter.GetBytes((short)date.DayOfYear).CopyTo(b, at);
        BitConverter.GetBytes((short)date.Year).CopyTo(b, at + 2);
    }
}
