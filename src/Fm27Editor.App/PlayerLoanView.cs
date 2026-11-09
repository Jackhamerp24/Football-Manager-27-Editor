using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>The Loan tab of a player on loan: the terms are editable and Recall sends him back to his contract club.</summary>
    Control LoanEditorPanel(PlayerRecord player, ActiveLoan loan, string borrowingClub)
    {
        var terms = loan.Terms;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 6), MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Left };
        var form = new UiKit.TwoColumnFormBuilder();
        var fields = new List<(TextBox Box, string Original)>();
        var save = new Button { IsVisible = false, Content = "Save loan", IsEnabled = false, Classes = { "accent" } };

        string Day(DateOnly? d) => DateSetting.Format(d);
        string Money(int v) => v < 0 ? "" : v.ToString(Inv);

        TextBox Field(string original, bool right, bool date = false)
        {
            var box = date
                ? new DateBox { Text = original, Width = 140, HorizontalContentAlignment = HorizontalAlignment.Right }
                : new TextBox { Text = original, Width = 140, HorizontalContentAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            box.TextChanged += (_, _) => save.IsEnabled = fields.Any(f => (f.Box.Text ?? "") != f.Original);
            fields.Add((box, original));
            return box;
        }

        form.AddRow(0, "Borrowing Club", new TextBox { Text = borrowingClub, Width = 196, IsReadOnly = true });
        var wage = Field(terms.Wage.ToString(Inv), true);
        var started = Field(Day(terms.Started), false, date: true);
        var signed = Field(Day(terms.Signed), false, date: true);
        var expires = Field(Day(terms.Expires), false, date: true);
        form.AddRow(0, "Wages (per week)", wage);
        form.AddRow(0, "Started", started);
        form.AddRow(0, "Signed", signed);
        form.AddRow(0, "Expires", expires);
        var price = Field(Money(terms.AgreedPrice), true);
        var buyBack = Field(Money(terms.BuyBackFee), true);
        var monthly = Field(Money(terms.MonthlyFee), true);
        var unused = Field(Money(terms.UnusedMonthlyFee), true);
        form.AddRow(1, "Agreed Price", price);
        form.AddRow(1, "Buy Back Fee", buyBack);
        form.AddRow(1, "Monthly Fee", monthly);
        form.AddRow(1, "Unused Monthly Fee", unused);
        panel.Children.Add(form.BuildCard("Loan Details"));

        bool Changed(TextBox box) => fields.First(f => ReferenceEquals(f.Box, box)).Box.Text != fields.First(f => ReferenceEquals(f.Box, box)).Original;

        save.Click += (_, _) =>
        {
            try
            {
                DateOnly? Date(TextBox box, string label)
                {
                    if (!Changed(box)) return null;
                    if (!DateSetting.TryParse((box.Text ?? "").Trim(), out var d))
                        throw new FormatException($"{label}: enter a date like 2027-06-30");
                    return d;
                }
                int? Amount(TextBox box, string label, bool none)
                {
                    if (!Changed(box)) return null;
                    string text = (box.Text ?? "").Trim();
                    if (text.Length == 0) return none ? -1 : throw new FormatException($"{label}: enter an amount");
                    return int.TryParse(text, NumberStyles.Integer, Inv, out int v) && v >= 0 ? v : throw new FormatException($"{label}: enter a whole number of 0 or more");
                }
                var edit = new LoanTermsEdit(
                    Wage: Amount(wage, "Wages", false), Started: Date(started, "Started"), Expires: Date(expires, "Expires"), Signed: Date(signed, "Signed"),
                    AgreedPrice: Amount(price, "Agreed Buy Price", true), BuyBackFee: Amount(buyBack, "Buy-Back Fee", true),
                    MonthlyFee: Amount(monthly, "Monthly Fee", true), UnusedMonthlyFee: Amount(unused, "Unused Monthly Fee", true));
                backend.Edits.Execute("Player", player.DisplayName, "Loan", () => backend.Session.SetLoanTerms(loan, edit));
                status.Text = $"Saved {player.DisplayName}'s loan. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };

        string parent = backend.Session.TransferDeals.TeamName(loan.ParentTeam) ?? "contract club";
        var recall = new Button { Content = $"Recall to {parent}" };
        recall.Click += (_, _) =>
        {
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Loan: Recall", () => backend.Session.RecallLoan(player));
                clubCache = null;
                selected = backend.Players.Reload(player.Address);
                if (selected is not null) { selected.GameDate = backend.GameDate; ReplaceRow(selected); }
                status.Text = $"Recalled {player.DisplayName} to {parent}. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, recall } });
        return panel;
    }
}
