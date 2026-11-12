using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Clubs;
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
            if (!int.TryParse(Wages.Text, System.Globalization.NumberStyles.AllowThousands, Inv, out var w) || w is < 0 or > 3_500_000
                || !DateSetting.TryParse(Started.Text, out var s)
                || !DateSetting.TryParse(Expires.Text, out var e)
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
            Started = new DateBox { Width = 130, Text = D(contract.Started) },
            Expires = new DateBox { Width = 130, Text = D(contract.Expires) },
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

    static Border Card(Control child) => new()
    {
        Background = UiKit.Surface,
        BorderBrush = UiKit.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12, 10),
        Child = child,
    };

    static readonly object dealCandidateLock = new();
    static IReadOnlyList<ulong>? cachedDealCandidates;
    static DateTime lastDealScanUtc = DateTime.MinValue;

    /// <summary>FMRTE 27's Transfer Proposals: the offers FMRTE lists, each with its fee and offered contract editable.</summary>
    Control ProposalsPanel(PlayerRecord player)
    {
        var host = new Border { Margin = new Thickness(0, 4) };
        var loading = new TextBlock
        {
            Text = "Loading transfer proposals...",
            Foreground = Brush("#94A3B8"),
            FontSize = 13.5,
            Margin = new Thickness(4),
        };
        host.Child = loading;

        ulong playerAddress = player.Address;
        var session = backend.Session;

        // Two steps, so the list never waits for the heap scan: the proposals and offered contracts first, then the live deal
        // (found by scanning the heap, which takes a while) is added when it is found. Any failure is shown instead of leaving "Loading".
        void Show(Control content) => Avalonia.Threading.Dispatcher.UIThread.Post(() => host.Child = content);
        _ = Task.Run(() =>
        {
            IReadOnlyList<TransferProposal> proposals = [];
            IReadOnlyDictionary<int, OfferContract> contracts = new Dictionary<int, OfferContract>();
            TransferDeal? liveDeal = null;
            try
            {
                proposals = session.Profile.Proposals is null
                    ? []
                    : session.Proposals.Read(player).Where(p => TransferProposalReader.ShownByFmrte(p.Status)).ToList();
                // An offer made after Load Game can live only in the heap: the cached map misses it. The player's
                // id list still names it, so rescan once instead of showing an empty tab.
                if (proposals.Count == 0 && session.Profile.Proposals is not null && HasOfferIds(player))
                {
                    session.Proposals.Refresh();
                    proposals = session.Proposals.Read(player).Where(p => TransferProposalReader.ShownByFmrte(p.Status)).ToList();
                }
                contracts = session.Profile.FutureTransfers is null
                    ? new Dictionary<int, OfferContract>()
                    : session.FutureTransfers.OfferContracts(player);
                lock (dealCandidateLock)
                {
                    if (cachedDealCandidates is not null && (DateTime.UtcNow - lastDealScanUtc).TotalSeconds < 60)
                        liveDeal = session.TransferDeals.FindForPlayer(playerAddress + 0x288, cachedDealCandidates);
                }
            }
            catch (Exception ex)
            {
                Show(new TextBlock { Text = $"Transfer proposals could not be read: {UserMessage.For(ex)}", Foreground = Brush("#F87171"), FontSize = 13.5, Margin = new Thickness(4), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                return;
            }
            var firstView = BuildOnUi(proposals, contracts, liveDeal);
            Show(firstView);

            if (liveDeal is not null) return;
            try
            {
                // The scan runs outside the lock: the window checks the cache on its own thread and must not wait for it.
                var candidates = session.TransferDeals.Scan();
                lock (dealCandidateLock)
                {
                    cachedDealCandidates = candidates;
                    lastDealScanUtc = DateTime.UtcNow;
                }
                if (session.TransferDeals.FindForPlayer(playerAddress + 0x288, candidates) is { } found)
                    Show(BuildOnUi(proposals, contracts, found));
            }
            catch { /* the proposals are already shown; a failed scan only means no live deal is listed */ }
        });

        Control BuildOnUi(IReadOnlyList<TransferProposal> p, IReadOnlyDictionary<int, OfferContract> c, TransferDeal? deal) =>
            Avalonia.Threading.Dispatcher.UIThread.Invoke(() => BuildProposalsPanelContent(player, deal, p, c));
        return host;
    }

    Control BuildProposalsPanelContent(
        PlayerRecord player,
        TransferDeal? liveDeal,
        IReadOnlyList<TransferProposal> proposals,
        IReadOnlyDictionary<int, OfferContract> contracts)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;

        if (liveDeal is not null)
        {
            var deal = liveDeal;
            var fromName = session.TransferDeals.TeamName(deal.SellingTeam) ?? (player.ClubName ?? "Selling club");
            var toName = session.TransferDeals.TeamName(deal.BuyingTeam) ?? "Buying club";
            var dealBody = new StackPanel { Spacing = 8 };
            dealBody.Children.Add(new TextBlock
            {
                Text = $"Transfer · {fromName} → {toName} · {deal.Status}",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = Brush("#F1F5F9"),
            });

            var feeBox = new TextBox
            {
                Width = 140,
                Text = deal.Fee.ToString("N0", Inv),
                HorizontalContentAlignment = HorizontalAlignment.Right,
            };
            var applyFee = new Button { Content = "Apply Fee" };
            applyFee.Click += (_, _) =>
            {
                if (!int.TryParse(feeBox.Text, System.Globalization.NumberStyles.AllowThousands, Inv, out var v) || v < 0)
                {
                    status.Text = "Transfer Fee: enter a fee of 0 or more.";
                    return;
                }
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Page save", () => session.TransferDealWriter.SetFee(deal, deal.Fee, v));
                    lock (dealCandidateLock) { cachedDealCandidates = null; }
                    status.Text = "Saved transfer fee. Continue in FM to see updates.";
                    RenderEditor();
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };

            var feeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Transfer Fee (£)", Width = 120, VerticalAlignment = VerticalAlignment.Center },
                    feeBox,
                    applyFee
                }
            };
            dealBody.Children.Add(feeRow);

            var statusOptions = new (int Code, string Label)[]
            {
                (TransferDealStatus.OfferMade, "Offer made"),
                (TransferDealStatus.OfferAccepted, "Offer accepted"),
                (TransferDealStatus.PlayerAccepted, "Player accepted"),
                (TransferDealStatus.Rejected, "Rejected"),
                (TransferDealStatus.Cancelled, "Cancelled"),
                (TransferDealStatus.Confirmed, "Confirmed"),
            };
            int selStatusIdx = Array.FindIndex(statusOptions, s => s.Code == deal.StatusRaw);
            if (selStatusIdx < 0) selStatusIdx = 0;
            var statusCombo = new ComboBox
            {
                Width = 170,
                ItemsSource = statusOptions.Select(s => s.Label).ToArray(),
                SelectedIndex = selStatusIdx,
            };
            var applyStatus = new Button { Content = "Apply Status" };
            applyStatus.Click += (_, _) =>
            {
                if (statusCombo.SelectedIndex < 0 || statusCombo.SelectedIndex >= statusOptions.Length) return;
                int newStatus = statusOptions[statusCombo.SelectedIndex].Code;
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Page save", () => session.TransferDealWriter.SetStatus(deal, deal.StatusRaw, newStatus));
                    lock (dealCandidateLock) { cachedDealCandidates = null; }
                    status.Text = "Saved deal status. Continue in FM to see updates.";
                    RenderEditor();
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Status", Width = 120, VerticalAlignment = VerticalAlignment.Center },
                    statusCombo,
                    applyStatus
                }
            };

            if (deal.IsOpen)
            {
                var acceptBtn = new Button { Content = "Accept Offer", Classes = { "accent" } };
                acceptBtn.Click += (_, _) =>
                {
                    try
                    {
                        backend.Edits.Execute("Player", player.DisplayName, "Page save", () => session.TransferDealWriter.AcceptOffer(deal));
                        lock (dealCandidateLock) { cachedDealCandidates = null; }
                        status.Text = "Offer accepted. Continue in FM to see updates.";
                        RenderEditor();
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                actionRow.Children.Add(acceptBtn);
            }
            dealBody.Children.Add(actionRow);

            panel.Children.Add(Card(dealBody));
        }

        if (liveDeal is null && proposals.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No active transfer proposals.", Foreground = Brush("#94A3B8"), FontSize = 13.5, Margin = new Thickness(4) });
            return panel;
        }

        if (proposals.Count == 0) return panel;

        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var fees = new List<(TransferProposal Proposal, TextBox Fee)>();
        var editors = new List<OfferContractEditor>();
        void Refresh() => save.IsEnabled =
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
                if (e.Parse() is not { } fields) { status.Text = "Contract: enter wages from 0 to 3,500,000 and dates like 2027-06-30, expiry after start"; return; }
                contractChanges.Add((e.Contract, fields));
            }
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () =>
                {
                    foreach (var c in feeChanges) session.ProposalWriter.SetValue(c.Proposal, c.Value);
                    foreach (var c in contractChanges) session.FutureTransferWriter.SetContract(c.Contract, c.Fields);
                });
                lock (dealCandidateLock) { cachedDealCandidates = null; }
                status.Text = "Saved transfer proposals. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save } });
        return panel;
    }

    /// <summary>
    /// Renders active loan details using the reusable TwoColumnFormBuilder, matching the exact CONTRACT DETAILS visual style.
    /// </summary>
    Control LoanPanel(PlayerRecord player, CurrentLoanInspection loan)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 6), MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Left };
        var form = new UiKit.TwoColumnFormBuilder();

        var borrowingClub = backend.Session.TransferDeals.TeamName(loan.BorrowingTeamAddress)
            ?? loadedClubs.FirstOrDefault(c => c.Teams.Any(t => t.Address == loan.BorrowingTeamAddress))?.DisplayName
            ?? "Borrowing club";
        if (backend.Session.Loans.Read(player) is { } active) return LoanEditorPanel(player, active, borrowingClub);

        Control Text(string text, double width = 140, bool rightAlign = false)
        {
            var box = new TextBox
            {
                Text = text,
                Width = width,
                IsReadOnly = true,
                HorizontalContentAlignment = rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            return box;
        }

        form.AddRow(0, "Borrowing Club", Text(borrowingClub, 196));
        if (loan.Contract is { } c)
        {
            form.AddRow(0, "Wages (per week)", Text(c.Wage.ToString("N0", Inv), 140, rightAlign: true));
            form.AddRow(0, "Started", Text(D(c.Started), 140));
            form.AddRow(0, "Signed", Text(D(loan.Signed), 140));
            form.AddRow(0, "Expires", Text(D(c.Expires), 140));
        }
        else if (loan.Signed is { } signed)
        {
            form.AddRow(0, "Signed", Text(D(signed), 140));
        }

        if (loan.Amounts is { } a)
        {
            form.AddRow(1, "Agreed Buy Price", Text(a.AgreedPrice >= 0 ? a.AgreedPrice.ToString("N0", Inv) : "None", 140, rightAlign: true));
            form.AddRow(1, "Buy-Back Fee", Text(a.BuyBackFee >= 0 ? a.BuyBackFee.ToString("N0", Inv) : "None", 140, rightAlign: true));
            form.AddRow(1, "Monthly Fee", Text(a.MonthlyFee >= 0 ? a.MonthlyFee.ToString("N0", Inv) : "None", 140, rightAlign: true));
            form.AddRow(1, "Unused Monthly Fee", Text(a.UnusedMonthlyFee >= 0 ? a.UnusedMonthlyFee.ToString("N0", Inv) : "None", 140, rightAlign: true));
        }

        panel.Children.Add(form.BuildCard("Loan Details"));
        return panel;
    }

    /// <summary>
    /// The Contract tab. If the player is on loan, the Loan tab is displayed first by default, followed by Contract.
    /// If not on loan, no Loan tab is shown.
    /// </summary>
    Control ContractTabs(PlayerRecord player)
    {
        var session = backend.Session;
        var contractContent = ContractPanel(player);
        var host = new ContentControl { Content = contractContent };
        // The loan and future-contract tabs need the offer map, and resolving it is a heap scan (about ten seconds on the
        // first use). Opening Contract must not wait for it: show the contract at once and add the extra tabs when the
        // background read finds a loan or a future contract.
        _ = Task.Run(() =>
        {
            CurrentLoanInspection? loan = null;
            try { loan = session.ReadCurrentLoan(player); } catch { }
            bool onLoan = loan is { Status: CurrentLoanStatus.Present };

            ulong future = 0;
            if (session.Profile.FutureTransfers is not null && session.Profile.Proposals is not null && HasOfferIds(player))
            {
                var contracts = session.FutureTransfers.OfferContracts(player);
                future = session.FutureTransfers.Read(player, session.Proposals.Read(player))
                    .Select(t => contracts.GetValueOrDefault(t.Offer.Id)).FirstOrDefault(c => c is { Loan: false })?.Address ?? 0;
            }
            return (loan, onLoan, future);
        }).ContinueWith(t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (t.IsFaulted || TopLevel.GetTopLevel(host) is null) return;
            var (loan, onLoan, future) = t.Result;
            if (!onLoan && future == 0) return;

            var pages = new List<(string Title, Control Content)>();
            if (onLoan && loan is not null)
                pages.Add(("Loan", LoanPanel(player, loan)));

            host.Content = null;
            pages.Add(("Contract", contractContent));

            if (future != 0)
                pages.Add(("Future Contract", ContractPanel(player, future)));

            host.Content = UiKit.OptionTabs(pages);
        }));
        return host;
    }

    /// <summary>Contract / Future Contract, kept across saves and player changes like the outer tab.</summary>

    /// <summary>
    /// True when the player's offer-id list holds an id. Reads only the list header: resolving the offers means scanning
    /// the game's heap (about ten seconds), which must not happen while the tab rail is being built.
    /// </summary>
    bool HasOfferIds(PlayerRecord player)
    {
        var session = backend.Session;
        if (session.Profile.Proposals is not { } p) return false;
        Span<byte> ptr = stackalloc byte[8];
        if (!session.Memory.TryRead(player.Address + (ulong)p.PlayerIds, ptr)) return false;
        ulong header = BitConverter.ToUInt64(ptr);
        if (header == 0 || !session.Memory.TryRead(header, ptr)) return false;
        ulong begin = BitConverter.ToUInt64(ptr);
        if (!session.Memory.TryRead(header + 8, ptr)) return false;
        ulong end = BitConverter.ToUInt64(ptr);
        return begin != 0 && end >= begin + 4 && (end - begin) % 4 == 0 && (end - begin) / 4 <= 100;
    }

    /// <summary>The Transfer Proposals tab shows when the player's offer list has any id, or a live deal was already found.</summary>
    bool HasTransferProposals(PlayerRecord player)
    {
        var session = backend.Session;
        try { if (HasOfferIds(player)) return true; } catch { }
        lock (dealCandidateLock)
        {
            if (cachedDealCandidates is null || (DateTime.UtcNow - lastDealScanUtc).TotalSeconds >= 60) return false;
            try { return session.TransferDeals.FindForPlayer(player.Address + 0x288, cachedDealCandidates) is not null; } catch { return false; }
        }
    }
    bool HasFutureTransfer(PlayerRecord player)
    {
        var session = backend.Session;
        if (session.Profile.FutureTransfers is null || session.Profile.Proposals is not { } p) return false;
        if (!HasOfferIds(player) || !session.Proposals.HasCachedOffers) return false;
        return FutureTransferPanel(player) is not null;
    }

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
        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var rowsOf = new List<(FutureTransfer Transfer, TextBox Date, TextBox Fee, OfferContractEditor? Contract)>();
        void Refresh() => save.IsEnabled = rowsOf.Any(r => r.Date.Text != D(r.Transfer.Date)
            || r.Fee.Text != r.Transfer.Offer.Value.ToString("N0", Inv) || r.Contract?.Changed() == true);
        foreach (var t in transfers)
        {
            var date = new DateBox { Width = 130, Text = D(t.Date) };
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
            // Takes the agreed transfer back out of the game (both builds): the offer, its date and the player's two lists.
            var remove = new Button { Content = "Remove future transfer", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            var removing = t;
            remove.Click += (_, _) =>
            {
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Remove future transfer", () => session.RemoveFutureTransfer(player, removing));
                    selected = backend.Players.Reload(player.Address); if (selected is not null) ReplaceRow(selected);
                    pendingSection = "Contract"; RenderEditor();
                    status.Text = $"Removed the future transfer to {removing.Offer.To}. Continue in FM to see it.";
                }
                catch (Exception ex) { status.Text = $"Remove stopped: {UserMessage.For(ex)}"; }
            };
            body.Children.Add(remove);
            rowsOf.Add((t, date, fee, editor));
            panel.Children.Add(Card(body));
        }
        save.Click += (_, _) =>
        {
            var writes = new List<Action>();
            foreach (var r in rowsOf)
            {
                if (!DateSetting.TryParse(r.Date.Text, out var d)
                    || !int.TryParse(r.Fee.Text, System.Globalization.NumberStyles.AllowThousands, Inv, out var v) || v < 0)
                { status.Text = "Future Transfer: enter a date like 2027-07-01 and a fee of 0 or more"; return; }
                var t = r.Transfer;
                if (d != t.Date) writes.Add(() => session.FutureTransferWriter.SetDate(t, d));
                if (v != t.Offer.Value) writes.Add(() => session.ProposalWriter.SetValue(t.Offer, v));
                if (r.Contract is { } e && e.Changed())
                {
                    if (e.Parse() is not { } fields) { status.Text = "Contract: enter wages from 0 to 3,500,000 and dates like 2027-06-30, expiry after start"; return; }
                    writes.Add(() => session.FutureTransferWriter.SetContract(e.Contract, fields));
                }
            }
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () => { foreach (var w in writes) w(); });
                status.Text = "Saved future transfer. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save } });
        return panel;
    }
}
