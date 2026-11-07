using System.Text.Json;

namespace Fm27Editor.Core.Offsets;

/// <summary>
/// Finances → Income Statement. The club finance object holds a run of monthly period blocks; each block keeps the
/// expenditure figures first and the income figures <see cref="IncomeBase"/> bytes later.
/// Confirmed on Manchester United (680), game date 25/11/2026, against FMRTE 27's Income Statement columns.
/// </summary>
public sealed record ClubStatementOffsets(
    int FinancePointer, int Base, int Stride, int Periods, int IncomeBase,
    IReadOnlyList<int> Income, IReadOnlyList<int> Expenditure)
{
    /// <summary>Income fields in the order FMRTE lists them; the array itself carries the offsets.</summary>
    public static readonly string[] IncomeKeys =
    [
        "gateReceipts", "other", "seasonTickets", "interest", "investments",
        "merchandising", "playersSold", "sponsorship", "matchDayIncome", "grants",
    ];

    /// <summary>Expenditure fields in the order FMRTE lists them.</summary>
    public static readonly string[] ExpenditureKeys =
    [
        "other", "loanRepayment", "leagueFines", "groundMaintenance", "playerWages",
        "staffWages", "bonuses", "loyaltyBonuses", "transferExpenditure", "vat", "niEmployerTax",
    ];

    internal static ClubStatementOffsets? Load(JsonElement parent)
    {
        if (!parent.TryGetProperty("statements", out var e)) return null;
        int Hex(string key) => Convert.ToInt32(
            e.GetProperty(key).GetString() is { } s && s.StartsWith("0x") ? s[2..]
                : throw new FormatException($"club.financeLists.statements.{key}: expected hex offset"), 16);
        int[] Block(string name, string[] keys)
        {
            var block = e.GetProperty(name);
            return keys.Select(k => Convert.ToInt32(
                block.GetProperty(k).GetString() is { } s && s.StartsWith("0x") ? s[2..]
                    : throw new FormatException($"club.financeLists.statements.{name}.{k}: expected hex offset"), 16)).ToArray();
        }
        return new ClubStatementOffsets(Hex("financePointer"), Hex("base"), Hex("stride"),
            e.GetProperty("periods").GetInt32(), Hex("incomeBase"),
            Block("income", IncomeKeys), Block("expenditure", ExpenditureKeys));
    }

    internal void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject("statements");
        w.WriteString("financePointer", $"0x{FinancePointer:X}");
        w.WriteString("base", $"0x{Base:X}");
        w.WriteString("stride", $"0x{Stride:X}");
        w.WriteNumber("periods", Periods);
        w.WriteString("incomeBase", $"0x{IncomeBase:X}");
        w.WriteStartObject("income");
        for (int i = 0; i < IncomeKeys.Length; i++) w.WriteString(IncomeKeys[i], $"0x{Income[i]:X}");
        w.WriteEndObject();
        w.WriteStartObject("expenditure");
        for (int i = 0; i < ExpenditureKeys.Length; i++) w.WriteString(ExpenditureKeys[i], $"0x{Expenditure[i]:X}");
        w.WriteEndObject();
        w.WriteEndObject();
    }
}
