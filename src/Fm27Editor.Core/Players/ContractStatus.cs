namespace Fm27Editor.Core.Players;

/// <summary>Contract page "Transfer Status", as FMRTE offers it.</summary>
public enum TransferListing { None, Listed, ByRequest, NotForSale }

/// <summary>Contract page "Loan Status", as FMRTE offers it.</summary>
public enum LoanListing { None, Listed, NotForLoan }

/// <summary>
/// The contract's status byte (profile <c>loanStatusOffset</c>, 27.0.1: +0x57). Each bit was found by changing Leny Yoro
/// in FMRTE 27 on Steam 27.0.1 and reading the byte after every Save (19/12/2026): Transfer Status Listed set 0x01,
/// By request 0x08, Not for Sale 0x10; Loan Status Listed is 0x02 and Not for Loan 0x40. FMRTE also sets 0x04 when it
/// writes Transfer Status None, a bit no player in the save has on his own, so this code leaves 0x04 alone and treats
/// None as "none of the three transfer bits". Mikel Merino (FMRTE: Listed) holds 0x01; Ibrahima Konate (None) 0x00.
/// </summary>
public static class ContractStatusFlags
{
    public const byte TransferListed = 0x01, LoanListed = 0x02, ByRequest = 0x08, NotForSale = 0x10, NotForLoan = 0x40;
    const byte TransferBits = TransferListed | ByRequest | NotForSale, LoanBits = LoanListed | NotForLoan;

    public static TransferListing Transfer(byte flags) =>
        (flags & TransferListed) != 0 ? TransferListing.Listed
        : (flags & ByRequest) != 0 ? TransferListing.ByRequest
        : (flags & NotForSale) != 0 ? TransferListing.NotForSale
        : TransferListing.None;

    /// <summary>Listed wins over Not for Loan, as FMRTE shows Yoro's 0x42 as Listed.</summary>
    public static LoanListing Loan(byte flags) =>
        (flags & LoanListed) != 0 ? LoanListing.Listed : (flags & NotForLoan) != 0 ? LoanListing.NotForLoan : LoanListing.None;

    public static byte WithTransfer(byte flags, TransferListing value) => (byte)((flags & ~TransferBits) | value switch
    {
        TransferListing.Listed => TransferListed,
        TransferListing.ByRequest => ByRequest,
        TransferListing.NotForSale => NotForSale,
        _ => 0,
    });

    public static byte WithLoan(byte flags, LoanListing value) => (byte)((flags & ~LoanBits) | value switch
    {
        LoanListing.Listed => LoanListed,
        LoanListing.NotForLoan => NotForLoan,
        _ => 0,
    });
}
