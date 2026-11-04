using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    static readonly ContractType[] OfferContractTypes = [ContractType.PartTime, ContractType.FullTime, ContractType.Amateur, ContractType.Youth, ContractType.NonContract];

    /// <summary>FMRTE's Contract Details box for an offered contract: Type, Wages, Started, Expires, On Rolling Contract.</summary>
    sealed class OfferContractEditor
    {
        public required OfferContract Contract;
        public required ComboBox Type;
        public required TextBox Wages, Started, Expires;
        public required CheckBox Rolling;

        public bool Changed() => Wages.Text != Contract.Wage.ToString("N0", Inv) || Started.Text != D(Contract.Started) || Expires.Text != D(Contract.Expires)
            || Rolling.IsChecked != Contract.Rolling || Type.SelectedIndex != Array.IndexOf(OfferContractTypes, (ContractType)Contract.Type);

        public OfferContractFields? Parse()
        {
            if (!int.TryParse(Wages.Text, System.Globalization.NumberStyles.AllowThousands, Inv, out var w) || w < 0
                || !DateOnly.TryParseExact(Started.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var s)
                || !DateOnly.TryParseExact(Expires.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var e)
                || e < s) return null;
            var type = Type.SelectedIndex >= 0 && Type.SelectedIndex < OfferContractTypes.Length ? OfferContractTypes[Type.SelectedIndex] : (ContractType)Contract.Type;
            return new(w, type, Rolling.IsChecked == true, s, e);
        }
    }

    OfferContractEditor OfferContractBox(OfferContract contract, Action changed, out Control view)
    {
        int ti = Array.IndexOf(OfferContractTypes, (ContractType)contract.Type);
        var e = new OfferContractEditor
        {
            Contract = contract,
            Type = new ComboBox { Width = 170, ItemsSource = OfferContractTypes.Select(ContractLabels.Of).ToArray(), SelectedIndex = ti },
            Wages = new TextBox { Width = 110, Text = contract.Wage.ToString("N0", Inv), HorizontalContentAlignment = HorizontalAlignment.Right },
            Started = new TextBox { Width = 110, Text = D(contract.Started) },
            Expires = new TextBox { Width = 110, Text = D(contract.Expires) },
            Rolling = new CheckBox { IsChecked = contract.Rolling },
        };
        e.Type.SelectionChanged += (_, _) => changed();
        foreach (var t in new[] { e.Wages, e.Started, e.Expires }) t.TextChanged += (_, _) => changed();
        e.Rolling.IsCheckedChanged += (_, _) => changed();
        view = Pairs(("Type", e.Type), ("Wages", e.Wages), ("Started", e.Started), ("Expires", e.Expires), ("On Rolling Contract", e.Rolling));
        return e;
    }

    static Grid Pairs(params (string Label, Control Value)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24, RowSpacing = 4 };
        for (int r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[r].Label, VerticalAlignment = VerticalAlignment.Center };
            rows[r].Value.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(label, r); Grid.SetRow(rows[r].Value, r); Grid.SetColumn(rows[r].Value, 1);
            grid.Children.Add(label); grid.Children.Add(rows[r].Value);
        }
        return grid;
    }

    static Border Card(Control child) => new() { Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8), Child = child };

    /// <summary>FMRTE 27's Transfer Proposals: the offers FMRTE lists, each with its fee and offered contract editable.</summary>
    Control ProposalsPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;
        if (session.Profile.Proposals is null) return panel;
        var proposals = session.Proposals.Read(player).Where(p => TransferProposalReader.ShownByFmrte(p.Status)).ToList();
        if (proposals.Count == 0) return panel;
        var contracts = session.Profile.FutureTransfers is null ? new Dictionary<int, OfferContract>() : session.FutureTransfers.OfferContracts(player);

        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        var fees = new List<(TransferProposal Proposal, TextBox Fee)>();
        var editors = new List<OfferContractEditor>();
        void Refresh() => save.IsEnabled = discard.IsEnabled =
            fees.Any(f => f.Fee.Text != f.Proposal.Value.ToString("N0", Inv)) || editors.Any(e => e.Changed());

        foreach (var p in proposals)
        {
            var fee = new TextBox { Width = 130, Text = p.Value.ToString("N0", Inv), HorizontalContentAlignment = HorizontalAlignment.Right };
            fee.TextChanged += (_, _) => Refresh();
            fees.Add((p, fee));
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = $"{ProposalLabels.Type(p.Type)} · {p.From} → {p.To} · {ProposalLabels.Status(p.Status)}", FontWeight = Avalonia.Media.FontWeight.SemiBold });
            body.Children.Add(Pairs(("Transfer Fee (£)", fee)));
            if (contracts.TryGetValue(p.Id, out var contract))
            {
                editors.Add(OfferContractBox(contract, Refresh, out var view));
                body.Children.Add(view);
            }
            panel.Children.Add(Card(body));
        }
        save.Click += (_, _) =>
        {
            var feeChanges = new List<(TransferProposal Proposal, int Value)>();
            foreach (var f in fees)
            {
                if (!int.TryParse(f.Fee.Text, System.Globalization.NumberStyles.AllowThousands, Inv, out var v) || v < 0) { status.Text = "Transfer Proposals: enter a fee of 0 or more"; return; }
                if (v != f.Proposal.Value) feeChanges.Add((f.Proposal, v));
            }
            var contractChanges = new List<(OfferContract Contract, OfferContractFields Fields)>();
            foreach (var e in editors.Where(e => e.Changed()))
            {
                if (e.Parse() is not { } fields) { status.Text = "Contract: enter wages of 0 or more and dates like 2027-06-30, expiry after start"; return; }
                contractChanges.Add((e.Contract, fields));
            }
            try
            {
                backend.Edits.Execute(() =>
                {
                    foreach (var c in feeChanges) session.ProposalWriter.SetValue(c.Proposal, c.Value);
                    foreach (var c in contractChanges) session.FutureTransferWriter.SetContract(c.Contract, c.Fields);
                });
                status.Text = "Saved transfer proposals. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderEditor();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }

    /// <summary>
    /// The Contract tab. Like FMRTE 27 (Ibrahim Dosso → Aston Villa), a player with a future transfer gets a second tab with
    /// the full contract editor on that move's contract.
    /// </summary>
    Control ContractTabs(PlayerRecord player)
    {
        var session = backend.Session;
        ulong future = 0;
        if (session.Profile.FutureTransfers is not null && session.Profile.Proposals is not null)
        {
            var contracts = session.FutureTransfers.OfferContracts(player);
            future = session.FutureTransfers.Read(player, session.Proposals.Read(player))
                .Select(t => contracts.GetValueOrDefault(t.Offer.Id)).FirstOrDefault(c => c is { Loan: false })?.Address ?? 0;
        }
        if (future == 0) return ContractPanel(player);
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Contract", Content = ContractPanel(player) },
                new TabItem { Header = "Future Contract", Content = ContractPanel(player, future) },
            },
            SelectedIndex = contractSubTab,
        };
        tabs.SelectionChanged += (_, _) => { if (tabs.SelectedIndex >= 0) contractSubTab = tabs.SelectedIndex; };
        return tabs;
    }

    /// <summary>Contract / Future Contract, kept across saves and player changes like the outer tab.</summary>
    int contractSubTab;

    /// <summary>FMRTE 27's Future Transfer tab: the agreed transfer date, fee and offered contract of a Confirmed offer.</summary>
    Control? FutureTransferPanel(PlayerRecord player)
    {
        var session = backend.Session;
        if (session.Profile.FutureTransfers is null || session.Profile.Proposals is null) return null;
        var proposals = session.Proposals.Read(player);
        var transfers = session.FutureTransfers.Read(player, proposals);
        if (transfers.Count == 0) return null;
        var contracts = session.FutureTransfers.OfferContracts(player);

        var panel = new StackPanel { Spacing = 8 };
        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        var rowsOf = new List<(FutureTransfer Transfer, TextBox Date, TextBox Fee, OfferContractEditor? Contract)>();
        void Refresh() => save.IsEnabled = discard.IsEnabled = rowsOf.Any(r => r.Date.Text != D(r.Transfer.Date)
            || r.Fee.Text != r.Transfer.Offer.Value.ToString("N0", Inv) || r.Contract?.Changed() == true);
        foreach (var t in transfers)
        {
            var date = new TextBox { Width = 110, Text = D(t.Date) };
            var fee = new TextBox { Width = 130, Text = t.Offer.Value.ToString("N0", Inv), HorizontalContentAlignment = HorizontalAlignment.Right };
            date.TextChanged += (_, _) => Refresh();
            fee.TextChanged += (_, _) => Refresh();
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(Pairs(("Player", new TextBlock { Text = player.DisplayName }), ("From", new TextBlock { Text = t.Offer.From ?? "" }),
                ("To", new TextBlock { Text = t.Offer.To ?? "" }), ("Transfer Date", date), ("Value", fee)));
            OfferContractEditor? editor = null;
            if (contracts.TryGetValue(t.Offer.Id, out var contract))
            {
                editor = OfferContractBox(contract, Refresh, out var view);
                body.Children.Add(new TextBlock { Text = $"Contract Details ({t.Offer.To})", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
                body.Children.Add(view);
            }
            rowsOf.Add((t, date, fee, editor));
            panel.Children.Add(Card(body));
        }
        save.Click += (_, _) =>
        {
            var writes = new List<Action>();
            foreach (var r in rowsOf)
            {
                if (!DateOnly.TryParseExact(r.Date.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var d)
                    || !int.TryParse(r.Fee.Text, System.Globalization.NumberStyles.AllowThousands, Inv, out var v) || v < 0)
                { status.Text = "Future Transfer: enter a date like 2027-07-01 and a fee of 0 or more"; return; }
                var t = r.Transfer;
                if (d != t.Date) writes.Add(() => session.FutureTransferWriter.SetDate(t, d));
                if (v != t.Offer.Value) writes.Add(() => session.ProposalWriter.SetValue(t.Offer, v));
                if (r.Contract is { } e && e.Changed())
                {
                    if (e.Parse() is not { } fields) { status.Text = "Contract: enter wages of 0 or more and dates like 2027-06-30, expiry after start"; return; }
                    writes.Add(() => session.FutureTransferWriter.SetContract(e.Contract, fields));
                }
            }
            try
            {
                backend.Edits.Execute(() => { foreach (var w in writes) w(); });
                status.Text = "Saved future transfer. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderEditor();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}
