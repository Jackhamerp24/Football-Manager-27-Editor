using System.Globalization;
using Fm27Editor.Core.Clubs;

namespace Fm27Editor.App;

public readonly record struct ClauseSaveValues(int? Main, bool ChangeExtra, int? ExtraNumber, DateOnly? ExtraDate);

/// <summary>Parses the two optional edits of a clause before any save action is executed.</summary>
public static class ClauseSaveInput
{
    public static ClauseSaveValues Parse(TransferClauseKind kind, bool changeMain, string? main, bool changeExtra, string? extra)
    {
        int? number = changeMain ? int.Parse(main!, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;
        int? extraNumber = null;
        DateOnly? extraDate = null;
        if (changeExtra)
        {
            switch (kind)
            {
                case TransferClauseKind.SellOn:
                    extraNumber = string.IsNullOrWhiteSpace(extra) ? null : int.Parse(extra.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case TransferClauseKind.AppearanceFee:
                    extraNumber = int.Parse(extra!, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case TransferClauseKind.CompetitionBonus:
                    extraDate = string.IsNullOrWhiteSpace(extra) ? null : DateOnly.ParseExact(extra.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        return new(number, changeExtra, extraNumber, extraDate);
    }
}
