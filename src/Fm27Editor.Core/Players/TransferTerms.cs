namespace Fm27Editor.Core.Players;

/// <summary>Observed transfer-offer terms; amounts are in the game's base currency.</summary>
/// <param name="Months">Total span of the instalments ("over 24 months").</param>
/// <param name="PeriodMonths">Months per instalment: 12 for Yearly, 6 for Every 6 months.</param>
/// <param name="WageContribution">Money the selling club pays, not the percentage typed in FMRTE.</param>
/// <param name="Friendly">Arrange a friendly between the clubs.</param>
public sealed record TransferTerms(int Instalments, byte Months, int LeagueApps, byte LeagueAppsCount,
    int InternationalApps, byte InternationalAppsCount, int PerLeagueApp, byte PerLeagueAppCount,
    int LeagueGoals, byte LeagueGoalsCount, int BuyBackFee, short SellOn, bool SellOnProfit, int WageContribution,
    byte PeriodMonths = 12, bool Friendly = false);

/// <summary>Experimental layouts observed in three FMRTE 27 Game Pass transfer-offer captures.</summary>
public static class TransferTermsLayout
{
    public const int Size = 208;

    /// <summary>Money the selling club pays for a share of the weekly wage. FMRTE stored 69,076 for 23% of 300,330 (69,075.9), so it rounds.</summary>
    public static int WageContribution(int weeklyWage, int percent) =>
        checked((int)Math.Round(weeklyWage * (double)percent / 100, MidpointRounding.AwayFromZero));

    /// <summary>Six optional vector payloads in the same order as their headers in the offer terms object.</summary>
    public static byte[][] Records(TransferTerms terms)
    {
        if (terms.Instalments < 0 || terms.LeagueApps < 0 || terms.InternationalApps < 0 || terms.PerLeagueApp < 0 ||
            terms.LeagueGoals < 0 || terms.BuyBackFee < 0 || terms.WageContribution < 0 || terms.SellOn < 0)
            throw new ArgumentOutOfRangeException(nameof(terms), "Amounts cannot be negative");
        byte[] Counted(int amount, byte count, int size)
        {
            if (amount == 0) return [];
            var data = new byte[size]; BitConverter.GetBytes(amount).CopyTo(data, 0); data[4] = count; data[5] = 0x7F;
            return data;
        }
        var instalments = Counted(terms.Instalments, terms.Months, 12);
        // Dembélé (Yearly) wrote 12 and Bellingham (Every 6 months) wrote 6; Bruno's save wrote 12 although
        // the request said six-monthly, so the game follows what FMRTE actually selected.
        if (instalments.Length != 0)
        {
            if (terms.PeriodMonths == 0) throw new ArgumentOutOfRangeException(nameof(terms), "PeriodMonths cannot be zero");
            BitConverter.GetBytes((int)terms.PeriodMonths).CopyTo(instalments, 8);
        }
        var buyBack = Counted(terms.BuyBackFee, 0xFF, 16);
        if (buyBack.Length != 0)
        {
            new byte[] { 1, 0, 0x6C, 7, 1, 0, 0x6C, 7 }.CopyTo(buyBack, 8);
        }
        return [instalments, Counted(terms.LeagueApps, terms.LeagueAppsCount, 8),
            Counted(terms.InternationalApps, terms.InternationalAppsCount, 8),
            Counted(terms.PerLeagueApp, terms.PerLeagueAppCount, 8),
            Counted(terms.LeagueGoals, terms.LeagueGoalsCount, 8), buyBack];
    }

    /// <summary>Builds the enclosing object using addresses allocated for the matching records.</summary>
    public static byte[] Object(TransferTerms terms, IReadOnlyList<ulong> addresses, IReadOnlyList<byte[]> records)
    {
        if (addresses.Count != 6 || records.Count != 6) throw new ArgumentException("Expected six vectors");
        var data = new byte[Size];
        for (int i = 0; i < 6; i++)
        {
            int length = records[i].Length; ulong start = addresses[i];
            if ((start == 0) != (length == 0)) throw new ArgumentException("Record pointer does not match its length");
            ulong end = checked(start + (ulong)length); int offset = i * 24;
            BitConverter.GetBytes(start).CopyTo(data, offset);
            BitConverter.GetBytes(end).CopyTo(data, offset + 8);
            BitConverter.GetBytes(end).CopyTo(data, offset + 16);
        }
        BitConverter.GetBytes(terms.WageContribution).CopyTo(data, 0xC0);
        // Sell-on: the percentage is one byte, at +0xC4 for "of the fee" and at +0xC5 for "Of the profit" (FMRTE 27 build8, 20% with the box
        // ticked and unticked); +0xC6 is Arrange a friendly (friendly on, profit off, 20% wrote C4=20 C6=1).
        if (terms.SellOn > 100) throw new ArgumentOutOfRangeException(nameof(terms), "Sell-on is a percentage");
        data[terms.SellOnProfit ? 0xC5 : 0xC4] = (byte)terms.SellOn;
        data[0xC6] = (byte)(terms.Friendly ? 1 : 0);
        return data;
    }
}
