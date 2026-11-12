using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Calibration;

/// <summary>
/// The text FMRTE 27 (27.0.1-build11) shows for coded values, read from its own dropdowns on 12/11/2026. Calibration
/// compares a manifest value copied from FMRTE with the label of the code the editor reads, so a wrong offset or a
/// wrong code shows up as a mismatch. Labels that FMRTE repeats for two codes ("Squad Player" 4 and 6) compare equal.
/// </summary>
public static class FmrteLabels
{
    /// <summary>Agreed / Previous / Future Playing Time, codes 0-19 in FMRTE's dropdown order.</summary>
    public static readonly string[] PlayingTime =
    [
        "Unset", "Star Player", "Important Player", "Regular Starter", "Squad Player", "Impact Sub", "Squad Player",
        "Fringe Player", "Fringe Player", "Emergency Backup", "Breakthrough Prospect", "Future Prospect", "Hot Prospect",
        "Youngster", "B Team Regular", "First-Choice Goalkeeper", "Cup Goalkeeper", "Domestic Cup Goalkeeper",
        "Continental Cup Goalkeeper", "Cup Goalkeeper",
    ];

    /// <summary>
    /// Unhappiness type in FMRTE's dropdown order, for codes 1-7 (the record's type byte): Tahith Chong's two motives
    /// hold 1 and FMRTE shows Concerned; Eduardo Camavinga's 802 holds 2 and FMRTE showed Internal.
    /// </summary>
    public static readonly string[] UnhappinessType =
        ["Concerned", "Internal", "Public", "Public", "Asked to leave", "Alienated", "Slight Concern"];

    public static string Of(PlayingTime? value) =>
        value is { } v && (int)v < PlayingTime.Length ? PlayingTime[(int)v] : value is null ? "" : $"code {(int)value}";

    public static string Of(TransferListing? value) => value switch
    {
        TransferListing.Listed => "Listed",
        TransferListing.ByRequest => "By request",
        TransferListing.NotForSale => "Not for Sale",
        TransferListing.None => "None",
        _ => "",
    };

    public static string Of(LoanListing? value) => value switch
    {
        LoanListing.Listed => "Listed",
        LoanListing.NotForLoan => "Not for Loan",
        LoanListing.None => "None",
        _ => "",
    };

    public static string UnhappinessTypeOf(byte code) => code is >= 1 && code <= UnhappinessType.Length ? UnhappinessType[code - 1] : $"Type {code}";

    /// <summary>
    /// Transfer Proposals status text, only for codes matched against FMRTE: 5 Seller accepted (Kylian Mbappé,
    /// Real Madrid to Man Utd) and 8 Player accepted (Ivan Perišić, PSV to Man Utd), both on 12/11/2026.
    /// </summary>
    public static string ProposalStatusOf(int code) => code switch
    {
        5 => "Seller accepted",
        8 => "Player accepted",
        _ => $"code {code}",
    };

    public static string Of(ProposalType type) => type switch
    {
        ProposalType.BClubMove => "B Club Move",
        _ => type.ToString(),
    };
}
