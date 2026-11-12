using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Bytes at contract +0x57 on Steam 27.0.1, read after each FMRTE 27 Save on Leny Yoro (19/12/2026): start 0x42
/// (Transfer None, Loan Listed), Listed 0x03, By request 0x0A, Not for Sale 0x12, None 0x06, Not for Loan 0x44.
/// Mikel Merino (FMRTE Transfer Listed) 0x01, Ibrahima Konate (None) 0x00.
/// </summary>
public class ContractStatusFlagsTests
{
    [Theory]
    [InlineData(0x42, TransferListing.None, LoanListing.Listed)]
    [InlineData(0x03, TransferListing.Listed, LoanListing.Listed)]
    [InlineData(0x0A, TransferListing.ByRequest, LoanListing.Listed)]
    [InlineData(0x12, TransferListing.NotForSale, LoanListing.Listed)]
    [InlineData(0x06, TransferListing.None, LoanListing.Listed)]
    [InlineData(0x44, TransferListing.None, LoanListing.NotForLoan)]
    [InlineData(0x01, TransferListing.Listed, LoanListing.None)]
    [InlineData(0x00, TransferListing.None, LoanListing.None)]
    public void Reads_what_FMRTE_shows(byte flags, TransferListing transfer, LoanListing loan)
    {
        Assert.Equal(transfer, ContractStatusFlags.Transfer(flags));
        Assert.Equal(loan, ContractStatusFlags.Loan(flags));
    }

    [Theory]
    [InlineData(0x42, TransferListing.Listed, 0x43)]      // FMRTE wrote 0x03: it also cleared 0x40; the editor keeps unrelated bits
    [InlineData(0x03, TransferListing.ByRequest, 0x0A)]
    [InlineData(0x0A, TransferListing.NotForSale, 0x12)]
    [InlineData(0x12, TransferListing.None, 0x02)]
    public void Setting_transfer_status_changes_only_the_transfer_bits(byte flags, TransferListing value, byte expected)
    {
        Assert.Equal(expected, ContractStatusFlags.WithTransfer(flags, value));
    }

    [Theory]
    [InlineData(0x06, LoanListing.NotForLoan, 0x44)]
    [InlineData(0x44, LoanListing.Listed, 0x06)]
    [InlineData(0x42, LoanListing.None, 0x00)]
    public void Setting_loan_status_changes_only_the_loan_bits(byte flags, LoanListing value, byte expected)
    {
        Assert.Equal(expected, ContractStatusFlags.WithLoan(flags, value));
    }
}
