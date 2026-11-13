using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed class TalentedRegensTab
{
    public sealed record RegenRow(
        PlayerRecord Player,
        string Name,
        int? Age,
        short CA,
        short PA,
        int RCA,
        string BestRoleCode,
        double BestRoleScore,
        string Personality,
        bool IsNegativePersonality,
        string Positions,
        string Club,
        string Nationality);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly Action<PlayerRecord> openPlayer;
    readonly TextBox minPaBox = new() { Text = "150", Width = 54, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox maxAgeBox = new() { Text = "23", Width = 48, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly CheckBox freeAgentBox = new() { Content = "Free agents only", VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox genderCombo = new() { ItemsSource = new[] { "All", "Men", "Women" }, SelectedIndex = 0, Width = 96 };
    readonly TextBox searchBox = new() { Watermark = "Filter name, club, nation, pos...", Width = 220 };
    readonly TextBlock summaryText = new() { FontSize = 12, Foreground = UiKit.Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center };
    readonly DataGrid grid = ToolGrid.Create();

    IReadOnlyList<PlayerRecord> loadedPlayers = [];

    public Control Root { get; }

    public TalentedRegensTab(Action<PlayerRecord> openPlayer)
    {
        this.openPlayer = openPlayer;

        var refreshBtn = new Button { Content = "Refresh" };
        refreshBtn.Click += (_, _) => ApplyFilter();

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        void AddField(string label, Control c)
        {
            toolbar.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Margin = new Thickness(0, 0, 12, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = label, FontSize = 12, Foreground = UiKit.Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center },
                    c,
                },
            });
        }

        AddField("Min PA", minPaBox);
        AddField("Max Age", maxAgeBox);
        AddField("Gender", genderCombo);
        toolbar.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 4), Child = freeAgentBox });
        toolbar.Children.Add(new Border { Margin = new Thickness(0, 0, 8, 4), Child = searchBox });
        toolbar.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 4), Child = refreshBtn });
        toolbar.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center, Child = summaryText });

        grid.Columns.Add(ToolGrid.Text<RegenRow>("Name", r => r.Name, 190, "Player name (double-click to open)", "#E2E8F0", bold: true));
        grid.Columns.Add(ToolGrid.Number<RegenRow>("Age", r => r.Age?.ToString(Inv) ?? "", 52, "Current age"));
        grid.Columns.Add(ToolGrid.Number<RegenRow>("CA", r => r.CA.ToString(Inv), 54, "Current Ability"));
        grid.Columns.Add(ToolGrid.Number<RegenRow>("PA", r => r.PA.ToString(Inv), 54, "Potential Ability"));
        grid.Columns.Add(ToolGrid.Number<RegenRow>("RCA", r => r.RCA.ToString(Inv), 54, "Recommended Current Ability"));
        grid.Columns.Add(ToolGrid.Text<RegenRow>("Role", r => r.BestRoleCode, 76, "Best fit tactical role", "#C084FC", bold: true));
        grid.Columns.Add(ToolGrid.Number<RegenRow>("Role %", r => r.BestRoleScore.ToString("0.0", Inv) + "%", 68, "Best role score"));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = ToolGrid.Header("Personality", "Classified personality (red = negative)", HorizontalAlignment.Left),
            Width = new DataGridLength(145),
            CellTemplate = new FuncDataTemplate<RegenRow>((r, _) => new TextBlock
            {
                Text = r?.Personality ?? "",
                FontWeight = r?.IsNegativePersonality == true ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = UiKit.Brush(r?.IsNegativePersonality == true ? "#F87171" : "#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            }),
        });
        grid.Columns.Add(ToolGrid.Text<RegenRow>("Pos", r => r.Positions, 105, "Natural / accomplished positions"));
        grid.Columns.Add(ToolGrid.Text<RegenRow>("Club", r => r.Club, 160, "Current club (blank = free agent)"));
        grid.Columns.Add(ToolGrid.Text<RegenRow>("Nat", r => r.Nationality, 115, "Nationality"));

        ToolGrid.EnableSort<RegenRow>(grid, new Dictionary<string, Func<RegenRow, IComparable?>>
        {
            ["Name"] = r => r.Name,
            ["Age"] = r => r.Age,
            ["CA"] = r => r.CA,
            ["PA"] = r => r.PA,
            ["RCA"] = r => r.RCA,
            ["Role"] = r => r.BestRoleCode,
            ["Role %"] = r => r.BestRoleScore,
            ["Personality"] = r => r.Personality,
            ["Pos"] = r => r.Positions,
            ["Club"] = r => r.Club,
            ["Nat"] = r => r.Nationality,
        });

        grid.DoubleTapped += (_, e) =>
        {
            if (UiKit.OnRow(e) && grid.SelectedItem is RegenRow row)
                openPlayer(row.Player);
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && grid.SelectedItem is RegenRow row)
            {
                openPlayer(row.Player);
                e.Handled = true;
            }
        };

        minPaBox.TextChanged += (_, _) => ApplyFilter();
        maxAgeBox.TextChanged += (_, _) => ApplyFilter();
        freeAgentBox.IsCheckedChanged += (_, _) => ApplyFilter();
        genderCombo.SelectionChanged += (_, _) => ApplyFilter();
        searchBox.TextChanged += (_, _) => ApplyFilter();

        var dock = new DockPanel { Margin = new Thickness(12, 10) };
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(toolbar);
        dock.Children.Add(grid);
        Root = dock;

        summaryText.Text = "Load players to scan regens.";
    }

    public void SetPlayers(IEnumerable<PlayerRecord> players, int? regenIdFloorOverride = null)
    {
        loadedPlayers = players as IReadOnlyList<PlayerRecord> ?? players.ToArray();
        ApplyFilter(regenIdFloorOverride);
    }

    public TalentedRegenCriteria CurrentCriteria()
    {
        int minPa = int.TryParse(minPaBox.Text?.Trim(), Inv, out int pa) ? Math.Clamp(pa, 1, 200) : 150;
        int maxAge = int.TryParse(maxAgeBox.Text?.Trim(), Inv, out int age) ? Math.Clamp(age, 14, 50) : 23;
        Gender? gender = genderCombo.SelectedIndex switch
        {
            1 => Gender.Men,
            2 => Gender.Women,
            _ => null,
        };
        return new TalentedRegenCriteria(
            MinPotentialAbility: minPa,
            MaxAge: maxAge,
            FreeAgentsOnly: freeAgentBox.IsChecked == true,
            Gender: gender,
            Query: searchBox.Text);
    }

    public void ApplyFilter(int? regenIdFloorOverride = null)
    {
        if (loadedPlayers.Count == 0)
        {
            ToolGrid.SetItems(grid, Array.Empty<RegenRow>());
            summaryText.Text = "Load players to scan regens.";
            return;
        }

        var result = TalentedRegensQuery.Find(loadedPlayers, CurrentCriteria(), regenIdFloorOverride);
        if (result.RegenIdentificationUnavailable)
        {
            ToolGrid.SetItems(grid, Array.Empty<RegenRow>());
            summaryText.Foreground = UiKit.Brush("#FBBF24");
            summaryText.Text = "Regen ID floor unavailable for the current player list.";
            return;
        }

        var rows = result.Matches.Select(m => new RegenRow(
            m.Player,
            m.Player.DisplayName,
            m.Player.Age,
            m.Player.CurrentAbility,
            m.Player.PotentialAbility,
            m.Rca,
            m.BestRole.Role.Code,
            m.BestRole.Score,
            m.Personality.Name,
            m.Personality.IsNegative,
            m.Player.PositionSummary,
            m.Player.ClubName,
            m.Player.NationalityName)).ToList();

        ToolGrid.SetItems(grid, rows);
        summaryText.Foreground = UiKit.Brush("#94A3B8");
        summaryText.Text = $"{rows.Count} regen(s) matched (ID >= {result.RegenIdFloor})";
    }
}
