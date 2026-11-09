using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

/// <summary>
/// Which of a staff member's contracts a list acts on. A person can work for a club and for a national team at once; the Staff
/// list of a club works on the club contract only and the Staff list of a nation on the national contract only.
/// </summary>
internal interface IStaffContractScope
{
    string Name { get; }
    /// <summary>"" when the person has no such contract, "No limit" when it has no expiry, otherwise the date.</summary>
    string ExpiryText(StaffRecord staff);
    void Renew(StaffRecord staff, int years, DateOnly today);
    void Expire(StaffRecord staff, DateOnly today);
    void Shift(StaffRecord staff, int years);
}

internal sealed class ClubStaffScope(GameSession session) : IStaffContractScope
{
    public string Name => "Club";
    public string ExpiryText(StaffRecord staff) => session.StaffContracts.Read(staff) is { } c ? c.Expires is { } ends ? DateSetting.Format(ends) : "No limit" : "";
    public void Renew(StaffRecord staff, int years, DateOnly today) => session.StaffContracts.Renew(staff, Need(staff), years, today);
    public void Expire(StaffRecord staff, DateOnly today) => session.StaffContracts.Expire(staff, Need(staff), today);
    public void Shift(StaffRecord staff, int years) => session.StaffContracts.ShiftExpiry(staff, Need(staff), years);
    Fm27Editor.Core.Players.OfferContract Need(StaffRecord staff) =>
        session.StaffContracts.Read(staff) ?? throw new InvalidOperationException($"{staff.DisplayName} has no club contract");
}

internal sealed class NationStaffScope(GameSession session) : IStaffContractScope
{
    public string Name => "National";
    public string ExpiryText(StaffRecord staff) => session.NationStaffContracts.Read(staff) is { } c ? c.Expires is { } ends ? DateSetting.Format(ends) : "No limit" : "";
    public void Renew(StaffRecord staff, int years, DateOnly today) => session.NationStaffContracts.Renew(staff, Need(staff), years, today);
    public void Expire(StaffRecord staff, DateOnly today) => session.NationStaffContracts.Expire(staff, Need(staff), today);
    public void Shift(StaffRecord staff, int years) => session.NationStaffContracts.ShiftExpiry(staff, Need(staff), years);
    Fm27Editor.Core.Players.OfferContract Need(StaffRecord staff) =>
        session.NationStaffContracts.Read(staff) ?? throw new InvalidOperationException($"{staff.DisplayName} has no national contract");
}

/// <summary>One line of a Staff list (club or nation).</summary>
internal sealed record StaffListRow(StaffRecord Staff, string Job, string Team, string Nation, int? Age, int CA, int PA, string Expires)
{
    public string Name => Staff.DisplayName;
}

public sealed partial class MainWindow
{
    readonly Dictionary<string, HashSet<ulong>> staffTicks = [];
    /// <summary>
    /// The Staff table shared by clubs and nations: the squad table's look (tick column, header sorting) and a Tools card whose
    /// Renew / Make it expire / −1 / +1 Year act on the ticked rows and only on the contract <paramref name="scope"/> names.
    /// </summary>
    Control BuildStaffTable(IReadOnlyList<StaffListRow> source, IStaffContractScope scope, Action refresh)
    {
        var rows = source.ToList();
        // The ticks survive a Renew/Expire (which redraws the table), so a chosen row stays the chosen row.
        string tickKey = scope.Name + ":" + (source.Count > 0 ? source[0].Staff.Address : 0UL);
        if (!staffTicks.TryGetValue(tickKey, out var excluded)) staffTicks[tickKey] = excluded = [];
        Action? updateCount = null;
        var grid = ToolGrid.Create();
        grid.ItemsSource = rows;
        ToolGrid.AddTick<StaffListRow>(grid, excluded, r => r.Staff.Address, () => rows, () => updateCount?.Invoke());
        grid.Columns.Add(ToolGrid.Text<StaffListRow>("Job", r => r.Job, 150, "Job"));
        grid.Columns.Add(ToolGrid.Text<StaffListRow>("Name", r => r.Name, 0, "Staff name", "#F1F5F9", bold: true, minWidth: 130));
        grid.Columns.Add(ToolGrid.Number<StaffListRow>("Age", r => r.Age?.ToString(CultureInfo.InvariantCulture) ?? "", 41, "Age"));
        grid.Columns.Add(ToolGrid.Text<StaffListRow>("Team", r => r.Team, 110, "Team"));
        grid.Columns.Add(ToolGrid.Text<StaffListRow>("Nat", r => r.Nation, 118, "Nationality"));
        grid.Columns.Add(ToolGrid.Number<StaffListRow>("CA", r => r.CA.ToString(CultureInfo.InvariantCulture), 41, "Current Ability"));
        grid.Columns.Add(ToolGrid.Number<StaffListRow>("PA", r => r.PA.ToString(CultureInfo.InvariantCulture), 41, "Potential Ability"));
        grid.Columns.Add(ToolGrid.Number<StaffListRow>("Expires", r => r.Expires, 92, $"{scope.Name} contract expiry"));
        var keys = new Dictionary<string, Func<StaffListRow, IComparable?>>
        {
            ["Job"] = r => r.Job.ToLowerInvariant(),
            ["Name"] = r => r.Name.ToLowerInvariant(),
            ["Age"] = r => r.Age,
            ["Team"] = r => r.Team.ToLowerInvariant(),
            ["Nat"] = r => r.Nation.ToLowerInvariant(),
            ["CA"] = r => r.CA,
            ["PA"] = r => r.PA,
            ["Expires"] = r => r.Expires.Length > 0 && r.Expires != "No limit" ? r.Expires : null,
        };
        ToolGrid.EnableSort(grid, keys);
        grid.DoubleTapped += (_, e) =>
        {
            if (UiKit.OnRow(e) && grid.SelectedItem is StaffListRow r) OpenClubStaff(r.Staff);
        };

        var count = new TextBlock { FontSize = 12, Foreground = UiKit.Brush("#94A3B8"), Margin = new Thickness(1, 0, 0, 4) };
        updateCount = () =>
        {
            int included = rows.Count(r => !excluded.Contains(r.Staff.Address));
            count.Text = included == 1 ? "( 1 Staff )" : $"( {included} Staff )";
        };
        updateCount();

        void Run(string action, Action<StaffRecord, DateOnly> operation)
        {
            var chosen = rows.Where(r => !excluded.Contains(r.Staff.Address)).Select(r => r.Staff).ToList();
            if (chosen.Count == 0) { status.Text = "No staff ticked"; return; }
            DateOnly today;
            try { today = backend.Session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"); }
            catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
            int done = 0;
            var failed = new List<string>();
            foreach (var s in chosen)
            {
                try { backend.Edits.Execute("Staff", s.DisplayName, $"{scope.Name} contract: {action}", () => operation(s, today)); done++; }
                catch (Exception ex) { failed.Add($"{s.DisplayName}: {UserMessage.For(ex)}"); }
            }
            status.Text = failed.Count == 0
                ? $"{scope.Name} contract, {action}: {done} staff updated. Continue in FM to see updates."
                : $"{scope.Name} contract, {action}: {done} updated, {failed.Count} not changed ({string.Join("; ", failed.Take(2))}).";
            if (done > 0) refresh();
        }

        Button Tool(string text, string action, Action<StaffRecord, DateOnly> operation)
        {
            var b = new Button
            {
                Content = text, FontSize = 12.5, Height = 24, MinHeight = 24, Padding = new Thickness(3, 0),
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            b.Click += (_, _) => Run(action, operation);
            return b;
        }
        var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        var minus = Tool("−1 Year", "−1 Year", (s, _) => scope.Shift(s, -1));
        var plus = Tool("+1 Year", "+1 Year", (s, _) => scope.Shift(s, 1));
        Grid.SetColumn(minus, 0); Grid.SetColumn(plus, 1);
        pair.Children.Add(minus); pair.Children.Add(plus);
        var tools = new StackPanel
        {
            Spacing = 8, Margin = new Thickness(8, 8),
            Children = { count, Tool("Renew for 4 Years", "Renew for 4 Years", (s, today) => scope.Renew(s, 4, today)), Tool("Make it expire", "Make it expire", (s, today) => scope.Expire(s, today)), pair },
        };
        var card = UiKit.Card("Tools", tools);
        card.Width = 196;
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(10, 0, 0, 0);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(grid, 0); layout.Children.Add(grid);
        Grid.SetColumn(card, 1); layout.Children.Add(card);
        return layout;
    }

    Control IScreenHost.BuildStaffTable(IReadOnlyList<StaffListRow> rows, IStaffContractScope scope, Action refresh) => BuildStaffTable(rows, scope, refresh);
}
