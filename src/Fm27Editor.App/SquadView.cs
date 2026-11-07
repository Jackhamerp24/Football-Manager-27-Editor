using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    int selectedSquadTeamIndex;
    SquadPlayerRow? selectedSquadRow;

    bool HookClubSquadsView(ClubEntry club, IReadOnlyList<TeamEntry> orderedTeams, TeamEntry? activeTeam, ulong teamAddr)
    {
        if (selectedClubSection != ClubSection.Squads)
            return false;

        if (orderedTeams.Count == 0)
        {
            clubEditorPanel.Children.Add(new TextBlock
            {
                Text = "No teams available for this club.",
                Foreground = Brush("#94A3B8"),
                FontSize = 11.5,
                Margin = new Thickness(4, 12, 0, 0),
            });
            return true;
        }

        if (selectedSquadTeamIndex >= orderedTeams.Count)
            selectedSquadTeamIndex = 0;

        var teamBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
        };

        for (int i = 0; i < orderedTeams.Count; i++)
        {
            int teamIdx = i;
            var t = orderedTeams[i];
            string teamName = t.IsFirstTeam ? "First team" : $"Team {i + 1}";
            if (orderedTeams.Count == 2 && !t.IsFirstTeam) teamName = "Reserves";
            else if (orderedTeams.Count == 3)
            {
                if (i == 1) teamName = "U21";
                else if (i == 2) teamName = "U18";
            }

            bool isSelected = teamIdx == selectedSquadTeamIndex;
            var tBtn = new Button
            {
                Content = $"{teamName} ({t.SquadCount})",
                Height = 26,
                Padding = new Thickness(10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brush(isSelected ? "#2E2452" : "#141724"),
                Foreground = Brush(isSelected ? "#C4B5FD" : "#94A3B8"),
                BorderBrush = Brush(isSelected ? "#7C3AED" : "#24293D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
            };
            tBtn.Click += (_, _) =>
            {
                selectedSquadTeamIndex = teamIdx;
                selectedSquadRow = null;
                RenderClubEditor();
            };
            teamBar.Children.Add(tBtn);
        }

        clubEditorPanel.Children.Add(teamBar);

        var curTeam = orderedTeams[selectedSquadTeamIndex];
        var allLoadedPlayers = rows.OfType<PlayerRecord>().ToList();
        var squadPlayers = SquadAdapter.ReadSquad(backend.Session, curTeam.Address, allLoadedPlayers);
        AddSquadPresetButtons(squadPlayers);

        var playerFields = squadPlayers.ToDictionary(
            p => p.Address,
            p => SquadAdapter.ReadAllPlayerFields(backend.Session, p));

        DateOnly? gameDate = backend.Session.ReadGameDate();
        bool hasCondition = playerFields.Values.Any(f => f.ContainsKey(PlayerField.Condition));
        bool hasAskingPrice = playerFields.Values.Any(f => f.ContainsKey(PlayerField.AskingPrice));

        var squadRows = new List<SquadPlayerRow>(squadPlayers.Count);
        foreach (var p in squadPlayers)
        {
            var fields = playerFields.GetValueOrDefault(p.Address);
            long? cond = fields?.TryGetValue(PlayerField.Condition, out long c) == true ? c : null;
            long? price = fields?.TryGetValue(PlayerField.AskingPrice, out long pr) == true ? pr : null;

            var injuries = backend.Session.ReadInjuries(p);
            var bans = backend.Session.ReadBans(p);
            var unhappy = gameDate is { } today ? backend.Session.ReadUnhappiness(p).Where(m => m.IsActive(today)).ToList() : [];
            bool isInjured = injuries.Count > 0;
            bool isBanned = bans.Count > 0;
            string inf = SquadFormatters.FormatInfFlags(p, isInjured, isBanned, unhappy.Count > 0);
            var tips = new List<string>();
            if (isInjured)
            {
                tips.Add(string.Join(", ", injuries.Select(injury => !string.IsNullOrEmpty(injury.Name)
                    ? char.ToUpper(injury.Name[0]) + injury.Name[1..]
                    : "Injured")));
            }
            if (isBanned)
            {
                tips.Add(string.Join(", ", bans.Select(b => b.Matches > 0 ? $"{b.Matches} match ban" : "Ban")));
            }
            if (unhappy.Count > 0) tips.Add(string.Join(", ", unhappy.Select(m => UnhappinessLabels.Motive(m.Motive))));
            string infTip = string.Join("; ", tips);

            string pos = SquadFormatters.FormatPositions(p);
            int? squadNum = SquadAdapter.ReadSquadNumber(backend.Session, p);
            squadRows.Add(new SquadPlayerRow(p, squadNum, inf, pos, cond, price, "—", infTip));
        }

        var squadGrid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = false,
            SelectionMode = DataGridSelectionMode.Extended,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Brush("#1D2130"),
            RowHeight = 29,
            Background = Brush("#11131C"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ItemsSource = squadRows,
        };
        squadGrid.PointerMoved += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.GetCurrentPoint(squadGrid).Properties.IsLeftButtonPressed)
                return;
            var row = squadGrid.GetVisualsAt(e.GetPosition(squadGrid)).OfType<DataGridRow>().FirstOrDefault();
            if (row?.DataContext is SquadPlayerRow item && !squadGrid.SelectedItems.Contains(item))
                squadGrid.SelectedItems.Add(item);
        };

        squadGrid.Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 0)),
                new Setter(Layoutable.MinHeightProperty, 26.0),
            }
        });
        squadGrid.Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
            }
        });
        squadGrid.Styles.Add(new Style(x => x.OfType<DataGridCell>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            }
        });

        Control Header(string text, string tooltip, HorizontalAlignment align = HorizontalAlignment.Center)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = align,
                TextTrimming = TextTrimming.None,
            };
            ToolTip.SetTip(tb, tooltip);
            return tb;
        }

        DataGridTemplateColumn NumCol(string title, Func<SquadPlayerRow, string> selector, double width, string tooltip)
        {
            return new DataGridTemplateColumn
            {
                Header = Header(title, tooltip, HorizontalAlignment.Right),
                Width = new DataGridLength(width),
                CellTemplate = new FuncDataTemplate<SquadPlayerRow>((row, _) => new TextBlock
                {
                    Text = selector(row),
                    Foreground = Brush("#F1F5F9"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(0, 0, 5, 0),
                }),
            };
        }

        squadGrid.Columns.Add(NumCol("#", r => r.DisplayNumber, 32, "Squad Number"));

        squadGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = Header("Inf", "Status / Injuries / Bans"),
            Width = new DataGridLength(42),
            CellTemplate = new FuncDataTemplate<SquadPlayerRow>((row, _) =>
            {
                var tb = new TextBlock
                {
                    Text = row.InfFlags,
                    Foreground = Brush("#EF4444"),
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.None,
                };
                if (!string.IsNullOrEmpty(row.InfTooltip))
                    ToolTip.SetTip(tb, row.InfTooltip);
                return tb;
            }),
        });

        squadGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = Header("Name", "Player Name", HorizontalAlignment.Left),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 116,
            CellTemplate = new FuncDataTemplate<SquadPlayerRow>((row, _) =>
            {
                var tb = new TextBlock
                {
                    Text = row.DisplayName,
                    FontWeight = FontWeight.Medium,
                    Foreground = Brush("#F1F5F9"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 4, 0),
                };
                ToolTip.SetTip(tb, row.DisplayName);
                return tb;
            }),
        });

        squadGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = Header("Pos", "Positions", HorizontalAlignment.Left),
            Width = new DataGridLength(94),
            CellTemplate = new FuncDataTemplate<SquadPlayerRow>((row, _) =>
            {
                var tb = new TextBlock
                {
                    Text = row.FormattedPositions,
                    Foreground = Brush("#C4B5FD"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 4, 0),
                };
                ToolTip.SetTip(tb, row.FormattedPositions);
                return tb;
            }),
        });

        squadGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = Header("Nat", "Nationality", HorizontalAlignment.Left),
            Width = new DataGridLength(72),
            CellTemplate = new FuncDataTemplate<SquadPlayerRow>((row, _) =>
            {
                var tb = new TextBlock
                {
                    Text = row.DisplayNat,
                    Foreground = Brush("#CBD5E1"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 4, 0),
                };
                ToolTip.SetTip(tb, row.DisplayNat);
                return tb;
            }),
        });

        squadGrid.Columns.Add(NumCol("CA", r => r.DisplayCa, 36, "Current Ability"));
        squadGrid.Columns.Add(NumCol("PA", r => r.DisplayPa, 36, "Potential Ability"));
        squadGrid.Columns.Add(NumCol("Age", r => r.DisplayAge, 36, "Age"));
        if (hasCondition)
            squadGrid.Columns.Add(NumCol("Cond", r => r.DisplayCondition, 50, "Condition (0–10000)"));
        if (hasAskingPrice)
        {
            squadGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = Header("Price", "Asking Price (£)", HorizontalAlignment.Right),
                Width = new DataGridLength(84),
                CellTemplate = new FuncDataTemplate<SquadPlayerRow>((row, _) =>
                {
                    var tb = new TextBlock
                    {
                        Text = row.DisplayAskingPrice,
                        Foreground = Brush("#F1F5F9"),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(0, 0, 5, 0),
                    };
                    if (!string.IsNullOrEmpty(row.DisplayAskingPrice))
                        ToolTip.SetTip(tb, $"£{row.DisplayAskingPrice}");
                    return tb;
                }),
            });
        }

        squadGrid.DoubleTapped += (_, _) =>
        {
            if (squadGrid.SelectedItem is SquadPlayerRow r)
                OpenPlayerFromSquad(r.Player);
        };
        squadGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && squadGrid.SelectedItem is SquadPlayerRow r)
            {
                OpenPlayerFromSquad(r.Player);
                e.Handled = true;
            }
        };

        var actionsHost = new Border();

        void RefreshActionsPanel()
        {
            var target = selectedSquadRow?.Player;
            actionsHost.Child = CreatePlayerActionsPanel(
                player: target,
                isSquadMode: true,
                getSquadPlayers: () => squadPlayers,
                onStatus: msg => status.Text = msg,
                getSelectedPlayers: () => squadGrid.SelectedItems.OfType<SquadPlayerRow>().Select(r => r.Player).ToArray());
        }

        squadGrid.SelectionChanged += (_, _) =>
        {
            selectedSquadRow = squadGrid.SelectedItem as SquadPlayerRow;
            RefreshActionsPanel();
        };

        RefreshActionsPanel();

        var layoutGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        Grid.SetColumn(squadGrid, 0);
        layoutGrid.Children.Add(squadGrid);

        actionsHost.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(actionsHost, 1);
        layoutGrid.Children.Add(actionsHost);

        clubEditorPanel.Children.Add(layoutGrid);
        return true;
    }

    async void OpenPlayerFromSquad(PlayerRecord player)
    {
        ulong addr = player.Address;
        int? id = player.Id;

        if (rows.Count == 0 && backend.State.Status == ConnectionStatus.Connected)
            await LoadAsync();

        var target = rows.OfType<PlayerRecord>()
            .FirstOrDefault(p => p.Address == addr || (id is not null && p.Id == id))
            ?? player;

        updating = true;
        selected = target;
        grid.SelectedItem = target;
        updating = false;
        ShowDetailTab(target);
        status.Text = $"Opened {target.DisplayName}";
    }
}
