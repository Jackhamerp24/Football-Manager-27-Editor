using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    static string GoldenLedgerPath => System.IO.Path.Combine(AppContext.BaseDirectory, "golden-ledger.txt");

    sealed record GoldenBatchRow(PlayerRecord Player, int PaBefore, int CaBefore, int PaAfter, int CaAfter, string Reason, string? Detail)
    {
        public string Change => PaAfter == PaBefore && CaAfter == CaBefore ? "-" : $"{PaBefore} -> {PaAfter}";
    }

    sealed record WonderkidOption(WonderkidChance Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Batch Golden Generation for a youth squad: snapshot a baseline, preview the rolls, apply the plan.</summary>
    void ShowGoldenBatch(Func<IReadOnlyList<PlayerRecord>> getSquad, Action<string>? onStatus, Action? onApplied)
    {
        var session = backend.Session;
        var minPa = new NumericUpDown { Minimum = 1, Maximum = 200, Value = GoldenGenerationWriter.DefaultMinPa, Height = 24, MinHeight = 24, FontSize = 12.5, FormatString = "0" };
        var maxPa = new NumericUpDown { Minimum = 1, Maximum = 200, Value = GoldenGenerationWriter.DefaultMaxPa, Height = 24, MinHeight = 24, FontSize = 12.5, FormatString = "0" };
        var wonderkidOptions = new[]
        {
            new WonderkidOption(WonderkidChance.Off, "No wonderkid lift"),
            new WonderkidOption(WonderkidChance.Five, "5%"),
            new WonderkidOption(WonderkidChance.Ten, "10%"),
            new WonderkidOption(WonderkidChance.TwentyFive, "25%"),
            new WonderkidOption(WonderkidChance.Fifty, "50%"),
            new WonderkidOption(WonderkidChance.SeventyFive, "75%"),
            new WonderkidOption(WonderkidChance.NinetyNine, "99%"),
        };
        var wonderkid = new ComboBox { ItemsSource = wonderkidOptions, SelectedIndex = 0 };
        var maxAge = new NumericUpDown { Minimum = 0, Maximum = 60, Value = 0, Height = 24, MinHeight = 24, FontSize = 12.5, FormatString = "0" };
        ToolTip.SetTip(maxAge, "0 means no age limit");
        var baselineLabel = new TextBlock { Text = "Baseline: none (everyone is treated as new)", Foreground = UiKit.TextSecondary, FontSize = 12 };
        List<PlayerRecord>? baseline = null;
        var ledger = GoldenGenerationLedger.Load(GoldenLedgerPath);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, RowHeight = 27, Height = 320 };
        grid.Columns.Add(new DataGridTextColumn { Header = "Player", Binding = new Binding("Player.DisplayName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "PA", Binding = new Binding("Change"), Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Reason", Binding = new Binding("Reason"), Width = new DataGridLength(130) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new Binding("Detail"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        IReadOnlyList<GoldenGenerationEntry>? plan = null;

        GoldenGenerationRequest Request()
        {
            int min = (int)(minPa.Value ?? GoldenGenerationWriter.DefaultMinPa), max = (int)(maxPa.Value ?? GoldenGenerationWriter.DefaultMaxPa);
            int age = (int)(maxAge.Value ?? 0);
            return new()
            {
                Potential = GoldenGenerationPotential.Custom(min, max),
                Wonderkid = (wonderkid.SelectedItem as WonderkidOption)?.Value ?? WonderkidChance.Off,
                Regens = new() { MaxAge = age <= 0 ? null : age },
            };
        }

        void Preview()
        {
            if (busy) return;
            busy = true;
            try
            {
                var squad = getSquad();
                var batch = new GoldenGenerationBatch(session.Writer, session.Profile, backend.Edits.Journal);
                var facts = new GameGoldenGenerationFacts(session.NationLinks, session.Contracts);
                HashSet<int>? baselineIds = baseline is null ? null : GoldenGenerationRegens.Ids(baseline).ToHashSet();
                plan = batch.Plan(squad, Request(), baselineIds, ledger, facts);
                grid.ItemsSource = plan.Select(e => new GoldenBatchRow(e.Player, e.PaBefore, e.CaBefore, e.PaAfter, e.CaAfter,
                    e.Reason == GoldenGenerationSkipReason.None ? (e.WillWrite ? "Roll" : "Held") : e.Reason.ToString(), e.Detail)).ToList();
                int writes = plan.Count(e => e.WillWrite);
                onStatus?.Invoke($"Golden Batch preview: {writes} to roll, {plan.Count - writes} held back.");
            }
            catch (Exception ex) { onStatus?.Invoke($"Golden Batch: {ex.Message}"); }
            finally { busy = false; }
        }

        var snapshot = new Button { Content = "Snapshot baseline" };
        snapshot.Click += (_, _) =>
        {
            baseline = getSquad().ToList();
            baselineLabel.Text = $"Baseline: {baseline.Count} players ({DateTime.Now:HH:mm})";
        };
        var preview = new Button { Content = "Preview" };
        preview.Click += (_, _) => Preview();
        var apply = new Button { Content = "Apply" };
        apply.Click += (_, _) =>
        {
            if (busy) return;
            if (plan is null) Preview();
            if (plan is null || plan.Count(e => e.WillWrite) == 0) { onStatus?.Invoke("Golden Batch: nothing to apply."); return; }
            busy = true;
            try
            {
                var batch = new GoldenGenerationBatch(session.Writer, session.Profile, backend.Edits.Journal);
                var result = batch.Apply(plan, ledger);
                ledger.Save(GoldenLedgerPath);
                onStatus?.Invoke($"Golden Batch: {result.Written} rolled, {result.Skipped} skipped. Continue in FM to see updates.");
                onApplied?.Invoke();
                plan = null;
            }
            catch (Exception ex) { onStatus?.Invoke($"Golden Batch: {ex.Message}"); }
            finally { busy = false; }
        };

        var inputs = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        inputs.Children.Add(new TextBlock { Text = "PA", VerticalAlignment = VerticalAlignment.Center });
        inputs.Children.Add(minPa);
        inputs.Children.Add(maxPa);
        inputs.Children.Add(new TextBlock { Text = "Wonderkid", VerticalAlignment = VerticalAlignment.Center });
        inputs.Children.Add(wonderkid);
        inputs.Children.Add(new TextBlock { Text = "Max age", VerticalAlignment = VerticalAlignment.Center });
        inputs.Children.Add(maxAge);
        inputs.Children.Add(snapshot);
        inputs.Children.Add(preview);
        inputs.Children.Add(apply);
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(10) };
        stack.Children.Add(inputs);
        stack.Children.Add(baselineLabel);
        stack.Children.Add(grid);
        var window = new Window { Title = "Golden Batch — Preview", Width = 760, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = stack };
        window.Show();
    }
}
