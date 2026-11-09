using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Fm27Editor.App;
using Fm27Editor.Core.Clubs;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Fm27Editor.App.Tests;

public sealed class UiSnapshotTests
{
    static bool initialized;
    static void EnsureApp()
    {
        if (initialized) return;
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
            initialized = true;
        }
        catch
        {
            initialized = true;
        }
    }

    static void RunOnUi(Action action)
    {
        EnsureApp();
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Invoke(action);
        }
    }

    [Fact]
    public void TwoColumnFormBuilder_Creates_Valid_Structure()
    {
        RunOnUi(() =>
        {
            var form = new UiKit.TwoColumnFormBuilder();
            form.AddRow(0, "Wages", new TextBox { Text = "100,000", Width = 140 });
            form.AddRow(1, "Expires", new TextBox { Text = "2028-06-30", Width = 140 });
            var card = form.BuildCard("Contract Details");
            Assert.NotNull(card);
            var stack = Assert.IsType<StackPanel>(card.Child);
            var header = Assert.IsType<Border>(stack.Children[0]);
            var headerDock = Assert.IsType<DockPanel>(header.Child);
            var title = Assert.IsType<TextBlock>(headerDock.Children[1]);
            Assert.Equal("CONTRACT DETAILS", title.Text);
        });
    }

    [Fact]
    public void FinancialStatementCard_Creates_Valid_Structure()
    {
        RunOnUi(() =>
        {
            var card = UiKit.FinancialStatementCard.Create("Summary", ["This Month", "Last Month"],
            [
                ("Balance", [1000000L, 950000L], false),
                ("Profit", [50000L, -20000L], true)
            ]);
            Assert.NotNull(card);
            var stack = Assert.IsType<StackPanel>(card.Child);
            var header = Assert.IsType<Border>(stack.Children[0]);
            var headerDock = Assert.IsType<DockPanel>(header.Child);
            var title = Assert.IsType<TextBlock>(headerDock.Children[1]);
            Assert.Equal("SUMMARY", title.Text);
        });
    }

    [Fact]
    public void Render_Ui_Snapshots_To_Disk()
    {
        RunOnUi(() =>
        {
            string outDir = @"C:\Users\Duong\.gemini\antigravity\brain\e0639a3d-2028-4a5d-830e-4a8ed6138a59";

        // 1. Render Loan Form
        var loanForm = new UiKit.TwoColumnFormBuilder();
        Control ReadOnlyText(string text, double width = 140, bool rightAlign = false) => new TextBox
        {
            Text = text,
            Width = width,
            IsReadOnly = true,
            HorizontalContentAlignment = rightAlign ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
        };

        loanForm.AddRow(0, "Borrowing Club", ReadOnlyText("Manchester United", 196));
        loanForm.AddRow(0, "Wages (per week)", ReadOnlyText("£43,982 / week", 140, rightAlign: true));
        loanForm.AddRow(0, "Started", ReadOnlyText("2026-08-15", 140));
        loanForm.AddRow(0, "Signed", ReadOnlyText("2026-08-15", 140));
        loanForm.AddRow(0, "Expires", ReadOnlyText("2027-06-30", 140));
        loanForm.AddRow(1, "Agreed Buy Price", ReadOnlyText("£152,820,496", 140, rightAlign: true));
        loanForm.AddRow(1, "Buy-Back Fee", ReadOnlyText("None", 140, rightAlign: true));
        loanForm.AddRow(1, "Monthly Fee", ReadOnlyText("£150,000", 140, rightAlign: true));
        loanForm.AddRow(1, "Unused Monthly Fee", ReadOnlyText("£200,000", 140, rightAlign: true));

        var loanCard = loanForm.BuildCard("Loan Details");
        loanCard.Background = UiKit.Surface;
        loanCard.Width = 780;

        // 2. Render Financial Statements
        string[] headers = ["This Month", "Last Month", "This Season"];
        var summaryCard = UiKit.FinancialStatementCard.Create("Summary", headers,
        [
            ("Balance", [100632533L, 0L, 0L], false),
            ("Profit", [-28320348L, -29914904L, 238133135L], true)
        ]);
        var incomeCard = UiKit.FinancialStatementCard.Create("Income", headers,
        [
            ("Gate Receipts", [6108488L, 5078694L, 22515877L], false),
            ("Other", [0L, 21041L, 875535L], false),
            ("Season Tickets", [0L, 0L, 37053075L], false),
            ("Interest", [41667L, 32375L, 253424L], false),
            ("Investments", [0L, 0L, 0L], false),
            ("Merchandising", [3912013L, 4497382L, 21283756L], false),
            ("Players Sold", [68238L, 72723L, 361198L], false),
            ("Sponsorship", [0L, 98495L, 341498628L], false),
            ("Match Day Income", [202225L, 188836L, 990756L], false),
            ("Grants", [0L, 0L, 0L], false),
            ("Total Income", [10332631L, 9989546L, 424832249L], true),
        ]);
        var expenditureCard = UiKit.FinancialStatementCard.Create("Expenditure", headers,
        [
            ("Other", [7709L, 1537165L, 6529573L], false),
            ("Loan Repayments and Interest", [4256977L, 4256977L, 17027908L], false),
            ("League Fines", [0L, 0L, 0L], false),
            ("Ground Maintenance", [392128L, 616280L, 2766170L], false),
            ("Player Wages", [21359128L, 21359128L, 101433223L], false),
            ("Staff Wages", [2278776L, 2278776L, 10818968L], false),
            ("Bonuses", [3477750L, 5216523L, 16527837L], false),
            ("Loyalty Bonuses", [2623216L, 2623216L, 12447155L], false),
            ("Transfer Expenditure", [2152995L, 5416L, 2169243L], false),
            ("VAT", [2104300L, 2010969L, 16979037L], false),
            ("NI Employer Tax", [0L, 333554L, 1333974L], false),
            ("Total Expenditure", [38652979L, 39904450L, 186699114L], true),
        ]);

        var financePanel = new StackPanel { Spacing = 10, Width = 780, Background = UiKit.WindowBg, Children = { summaryCard, incomeCard, expenditureCard } };

        void SaveControlToPng(Control c, int width, int height, string filename)
        {
            var window = new Window
            {
                Width = width,
                Height = height,
                Background = UiKit.WindowBg,
                Padding = new Thickness(16),
                Content = c,
            };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            rtb.Render(window);
            string path = Path.Combine(outDir, filename);
            rtb.Save(path);
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        SaveControlToPng(loanCard, 800, 320, "review_loan_details.png");
        SaveControlToPng(financePanel, 800, 850, "review_income_statement.png");

        // 3. Render Club Vision
        var visionModel = ClubListBuilder.BuildVision(
        [
            new ClubVisionRow(104, 5, StartDate: new DateOnly(2026, 7, 1), EndDate: new DateOnly(2027, 6, 30), TypeName: ClubVisionTypes.DisplayName(104), Target: 21, SourceType: 1),
            new ClubVisionRow(84, 4, StartDate: new DateOnly(2026, 7, 1), EndDate: new DateOnly(2027, 6, 30), TypeName: ClubVisionTypes.DisplayName(84), Target: 3, SourceType: 2),
            new ClubVisionRow(2, 8, StartDate: new DateOnly(2026, 7, 1), EndDate: new DateOnly(2027, 6, 30), TypeName: ClubVisionTypes.DisplayName(2), Target: 4, SourceType: 1),
        ]);

        var visionGrid = new Grid { ColumnDefinitions = MainWindow.CreateClubListColumns(visionModel.Columns), RowSpacing = 6, Margin = new Thickness(12, 10) };
        int vr = 0;
        visionGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < visionModel.Columns.Count; i++)
        {
            var h = new TextBlock
            {
                Text = visionModel.Columns[i].Header,
                FontSize = 11,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = UiKit.TextMuted,
                TextAlignment = visionModel.Columns[i].RightAlign ? Avalonia.Media.TextAlignment.Right : Avalonia.Media.TextAlignment.Left,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
            };
            Grid.SetRow(h, 0); Grid.SetColumn(h, i); visionGrid.Children.Add(h);
        }
        foreach (var row in visionModel.Rows)
        {
            vr++;
            visionGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < row.Cells.Count; i++)
            {
                var col = visionModel.Columns[i];
                var cText = new TextBlock
                {
                    Text = row.Cells[i].Text,
                    FontSize = 11.5,
                    Foreground = UiKit.TextPrimary,
                    TextAlignment = col.RightAlign ? Avalonia.Media.TextAlignment.Right : Avalonia.Media.TextAlignment.Left,
                    Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                };
                Grid.SetRow(cText, vr); Grid.SetColumn(cText, i); visionGrid.Children.Add(cText);
            }
        }
        var visionTableCard = UiKit.Card("Club Vision", visionGrid);

        // Edit card matching ClubView's Detail panel
        var editPanel = new StackPanel { Spacing = 10, Margin = new Thickness(12, 10) };
        editPanel.Children.Add(new TextBlock { Text = "Sign players under the age of 21 for the first team", FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = UiKit.TextPrimary });
        var editRow1 = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Importance", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                new TextBox { Text = "5", Width = 60, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Right },
                new TextBlock { Text = "Source", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                new ComboBox { ItemsSource = new[] { "Board", "Supporters" }, SelectedIndex = 0, Width = 130 },
                new TextBlock { Text = "Start", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                new TextBox { Text = "2026-07-01", Width = 95 },
                new TextBlock { Text = "End", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                new TextBox { Text = "2027-06-30", Width = 95 },
            }
        };
        var editActions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Button { Content = "Save changes", Classes = { "accent" }, IsEnabled = true },
                new Button { Content = "Discard", IsEnabled = true },
                new Button { Content = "Remove", Margin = new Thickness(16, 0, 0, 0) },
            }
        };
        editPanel.Children.Add(editRow1);
        editPanel.Children.Add(editActions);
        var editCard = UiKit.Card("Edit Objective", editPanel);

        var visionCombined = new StackPanel { Spacing = 10, Width = 780, Background = UiKit.WindowBg, Children = { visionTableCard, editCard } };
        SaveControlToPng(visionCombined, 800, 360, "review_club_vision.png");

        // 4. Render Universal Search Overlay (Flat & Grouped)
        Control BuildSearchSample(bool grouped)
        {
            var searchBox = new TextBox
            {
                Text = "uefa",
                Height = 32,
                FontSize = 13,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            var closeBtn = new Button { Content = "Close", Height = 32, Margin = new Thickness(8, 0, 0, 0) };
            var fieldRow = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(closeBtn, Dock.Right);
            fieldRow.Children.Add(closeBtn);
            fieldRow.Children.Add(searchBox);

            var resultsRow = new DockPanel { LastChildFill = false, Margin = new Thickness(2, 6, 2, 6) };
            var leftSum = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            leftSum.Children.Add(new TextBlock { Text = "Results", FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = UiKit.TextPrimary, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            leftSum.Children.Add(new Border
            {
                Background = UiKit.Accent,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(6, 1),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock { Text = "31", FontSize = 10.5, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White }
            });
            resultsRow.Children.Add(leftSum);

            var rightActs = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            DockPanel.SetDock(rightActs, Dock.Right);
            rightActs.Children.Add(new Button
            {
                Content = grouped ? "Grouped ✓" : "Group by type",
                Classes = { "meta-link" },
                FontSize = 11.5,
                Foreground = grouped ? UiKit.Brush("#C084FC") : UiKit.TextMuted,
            });
            rightActs.Children.Add(new Button
            {
                Content = "Advanced search...",
                Classes = { "meta-link" },
                FontSize = 11.5,
                Foreground = UiKit.Brush("#A78BFA"),
            });
            resultsRow.Children.Add(rightActs);

            var list = new StackPanel { Spacing = 2 };

            var sampleItems = new (string Kind, string Name, string Meta)[]
            {
                ("COMPETITION", "UEFA Champions League", "ID 1301413 · Rep 195"),
                ("COMPETITION", "UEFA Women's Champions League", "ID 2000045610 · Rep 160"),
                ("COMPETITION", "UEFA Europa League", "ID 1301414 · Rep 175"),
                ("COMPETITION", "UEFA Super Cup", "ID 1301416 · Rep 165"),
                ("COMPETITION", "UEFA Conference League", "ID 2000021677 · Rep 145"),
                ("COMPETITION", "UEFA Youth League", "ID 2000009477 · Rep 110"),
                ("AWARD", "UEFA Champions League Best Player", "ID 2000180166 · Rep 15"),
                ("AWARD", "UEFA Champions League Best Goal", "ID 2000180167 · Rep 15"),
                ("AWARD", "UEFA Champions League Golden Boot", "ID 150936 · Rep 17"),
                ("PLAYER", "Mathieu Nguefack", "ID 2000331099 · Borussia M'gladbach"),
            };

            if (grouped)
            {
                var groups = sampleItems.GroupBy(x => x.Kind);
                foreach (var g in groups)
                {
                    string groupLabel = g.Key == "STAFF" ? "STAFF" : g.Key + "S";
                    var headerBar = new Border
                    {
                        Background = UiKit.SurfaceHeader,
                        BorderBrush = UiKit.BorderSubtle,
                        BorderThickness = new Thickness(0, 1, 0, 1),
                        Padding = new Thickness(10, 4),
                        Margin = new Thickness(0, 4, 0, 2),
                        CornerRadius = new CornerRadius(3),
                        Child = new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 6,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Children =
                            {
                                new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(1.5), Background = UiKit.Accent, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                                new TextBlock { Text = groupLabel, FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = UiKit.TextSecondary, LetterSpacing = 0.5 },
                                new Border
                                {
                                    Background = UiKit.AccentSoft,
                                    BorderBrush = UiKit.AccentBorder,
                                    BorderThickness = new Thickness(1),
                                    CornerRadius = new CornerRadius(8),
                                    Padding = new Thickness(6, 1),
                                    Child = new TextBlock { Text = g.Count().ToString(), FontSize = 10, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = UiKit.Brush("#E9D5FF") }
                                }
                            }
                        }
                    };
                    list.Children.Add(headerBar);
                    foreach (var item in g)
                    {
                        var rowDock = new DockPanel { LastChildFill = true };
                        rowDock.Children.Add(new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 8,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Children =
                            {
                                new TextBlock { Text = item.Name, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = UiKit.TextPrimary },
                                new TextBlock { Text = item.Meta, FontSize = 11, Foreground = UiKit.TextMuted },
                            }
                        });
                        list.Children.Add(new Border { Height = 28, Padding = new Thickness(8, 0), Child = rowDock });
                    }
                }
            }
            else
            {
                foreach (var item in sampleItems)
                {
                    var rowDock = new DockPanel { LastChildFill = true };
                    var pill = UiKit.MetaPill(item.Kind == "COMPETITION" ? "Competition" : item.Kind == "AWARD" ? "Award" : "Player", item.Kind == "COMPETITION");
                    DockPanel.SetDock(pill, Dock.Right);
                    rowDock.Children.Add(pill);
                    rowDock.Children.Add(new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = item.Name, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = UiKit.TextPrimary },
                            new TextBlock { Text = item.Meta, FontSize = 11, Foreground = UiKit.TextMuted },
                        }
                    });
                    list.Children.Add(new Border { Height = 28, Padding = new Thickness(8, 0), Child = rowDock });
                }
            }

            return new Border
            {
                Width = 720,
                Background = UiKit.Surface,
                BorderBrush = UiKit.BorderControl,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        fieldRow,
                        resultsRow,
                        new Border { BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = list }
                    }
                }
            };
        }

        SaveControlToPng(BuildSearchSample(false), 760, 460, "review_search_flat.png");
        SaveControlToPng(BuildSearchSample(true), 760, 520, "review_search_grouped.png");

        // 5. Render Club Affiliations View (Unified draft, No Details Box, No Apply Buttons)
        var affModel = ClubListBuilder.BuildAffiliations(
        [
            new ClubAffiliationRow("R. Madrid", "R. Madrid B", 4, new DateOnly(2024, 8, 3), ParentClubId: 1736, FeederClubId: 1737, AnnualFee: 0),
            new ClubAffiliationRow("R. Madrid", "R. Madrid C", 5, new DateOnly(2024, 7, 3), ParentClubId: 1736, FeederClubId: 1738, AnnualFee: 0),
            new ClubAffiliationRow("Borussia Dortmund", "R. Madrid", 0, new DateOnly(2024, 8, 13), ParentClubId: 1472, FeederClubId: 1736, AnnualFee: 0),
            new ClubAffiliationRow("R. Madrid", "Albacete", 0, new DateOnly(2024, 8, 3), ParentClubId: 1736, FeederClubId: 1660, AnnualFee: 0),
            new ClubAffiliationRow("FC Bayern München", "R. Madrid", 17, new DateOnly(2024, 6, 13), ParentClubId: 915, FeederClubId: 1736, AnnualFee: 0),
            new ClubAffiliationRow("Getafe", "R. Madrid", 0, new DateOnly(2024, 8, 3), ParentClubId: 1682, FeederClubId: 1736, AnnualFee: 0),
        ]);

        var affGrid = new Grid { ColumnDefinitions = MainWindow.CreateClubListColumns(affModel.Columns), Margin = new Thickness(4, 0, 16, 0) };
        var affHeaderGrid = new Grid { ColumnDefinitions = MainWindow.CreateClubListColumns(affModel.Columns) };
        for (int i = 0; i < affModel.Columns.Count; i++)
        {
            var h = new TextBlock
            {
                Text = affModel.Columns[i].Header,
                FontSize = 11.5,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = UiKit.Brush("#CBD5E1"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
            };
            Grid.SetColumn(h, i);
            affHeaderGrid.Children.Add(h);
        }
        var affListStack = new StackPanel { Spacing = 2 };
        for (int r = 0; r < affModel.Rows.Count; r++)
        {
            var row = affModel.Rows[r];
            var rGrid = new Grid { ColumnDefinitions = MainWindow.CreateClubListColumns(affModel.Columns), Margin = new Thickness(4, 0, 16, 0) };
            for (int i = 0; i < row.Cells.Count; i++)
            {
                var txt = new TextBlock
                {
                    Text = row.Cells[i].Text,
                    FontSize = 11.5,
                    Foreground = UiKit.TextPrimary,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                };
                Grid.SetColumn(txt, i);
                rGrid.Children.Add(txt);
            }
            var rowBorder = new Border
            {
                Height = 28,
                Background = r == 0 ? UiKit.Brush("#2E1065") : Avalonia.Media.Brushes.Transparent,
                Padding = new Thickness(8, 2),
                CornerRadius = new CornerRadius(3),
                Child = rGrid,
            };
            affListStack.Children.Add(rowBorder);
        }
        var affTableCard = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Background = UiKit.SurfaceHeader, BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 6, 24, 6), Child = affHeaderGrid },
                    new Border { Background = UiKit.Brush("#151926"), Padding = new Thickness(0), Child = affListStack }
                }
            }
        };

        // Edit controls (clean checkboxes, inputs, save/discard/remove, NO details card, NO apply buttons)
        var affEditPanel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 10) };
        var cbs = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, ItemSpacing = 16, LineSpacing = 8 };
        cbs.Children.Add(new CheckBox { Content = "Scouting Knowledge Shared", IsChecked = false, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        cbs.Children.Add(new CheckBox { Content = "Players Go On Loan", IsChecked = true, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        cbs.Children.Add(new CheckBox { Content = "First Option To Buy", IsChecked = false, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        cbs.Children.Add(new CheckBox { Content = "Play Friendlies", IsChecked = true, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        affEditPanel.Children.Add(cbs);

        var inputs = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, ItemSpacing = 16, LineSpacing = 8 };
        inputs.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            new TextBlock { Text = "% Loan Wage Paid By Parent", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            new TextBox { Text = "0", Width = 80, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Right }
        } });
        inputs.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            new TextBlock { Text = "Start Date", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            new TextBox { Text = "2024-08-03", Width = 110 }
        } });
        inputs.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            new TextBlock { Text = "End Date", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            new TextBox { Text = "2027-06-30", Width = 110 }
        } });
        inputs.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            new TextBlock { Text = "Annual Fee", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            new TextBox { Text = "0", Width = 100, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Right }
        } });
        affEditPanel.Children.Add(inputs);

        var acts = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Button { Content = "Save changes", Classes = { "accent" }, IsEnabled = true },
                new Button { Content = "Discard", IsEnabled = true },
                new Button { Content = "Remove", Margin = new Thickness(12, 0, 0, 0) }
            }
        };
        affEditPanel.Children.Add(acts);

        var affHost = new StackPanel { Spacing = 10, Width = 820, Background = UiKit.WindowBg, Children = { affTableCard, affEditPanel } };
        SaveControlToPng(affHost, 850, 420, "review_affiliations.png");

        // 6. Render Expanded Club Staff Table (Full Height, No squishing to 300px, No Open Selected button)
        static Fm27Editor.Core.Staff.StaffRecord MockStaff(ulong addr, int id, string first, string last, short job, short ca, short pa, DateOnly dob, string nat) => new()
        {
            Address = addr,
            StaffAddress = addr + 0x1000,
            Id = id,
            FirstName = first,
            LastName = last,
            JobCode = job,
            CurrentAbility = ca,
            PotentialAbility = pa,
            DateOfBirth = dob,
            Nationality = new Fm27Editor.Core.Players.NationInfo(796, nat, nat),
            Attributes = [],
            Personality = [],
            Kind = new Fm27Editor.Core.Staff.StaffKind(0, 0, 0, false),
        };

        var staffModel = ClubStaffLabels.BuildTable(
        [
            MockStaff(0x1000, 1, "Carlo", "Ancelotti", 16, 175, 185, new DateOnly(1959, 6, 10), "Italy"),
            MockStaff(0x1001, 2, "Davide", "Ancelotti", 20, 145, 160, new DateOnly(1989, 7, 22), "Italy"),
            MockStaff(0x1002, 3, "Antonio", "Pintus", 26, 190, 195, new DateOnly(1962, 9, 5), "Italy"),
            MockStaff(0x1003, 4, "Luis", "Llopis", 34, 165, 170, new DateOnly(1964, 10, 15), "Spain"),
            MockStaff(0x1004, 5, "Juni", "Calafat", 64, 195, 200, new DateOnly(1972, 8, 20), "Spain"),
            MockStaff(0x1005, 6, "Santiago", "Solari", 10, 155, 165, new DateOnly(1976, 10, 7), "Argentina"),
            MockStaff(0x1006, 7, "Raúl", "González", 2, 150, 170, new DateOnly(1977, 6, 27), "Spain"),
            MockStaff(0x1007, 8, "Niko", "Mihic", 60, 170, 175, new DateOnly(1964, 4, 12), "Spain"),
            MockStaff(0x1008, 9, "Felipe", "Sánchez", 60, 140, 150, new DateOnly(1982, 3, 1), "Spain"),
            MockStaff(0x1009, 10, "Manuel", "Delgado", 2, 160, 165, new DateOnly(1974, 5, 18), "Spain"),
        ], new DateOnly(2026, 11, 8));

        var staffHeader = new Grid { ColumnDefinitions = MainWindow.CreateClubListColumns(staffModel.Columns) };
        for (int i = 0; i < staffModel.Columns.Count; i++)
        {
            var h = new TextBlock
            {
                Text = staffModel.Columns[i].Header,
                FontSize = 11.5,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = UiKit.Brush("#CBD5E1"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
            };
            Grid.SetColumn(h, i);
            staffHeader.Children.Add(h);
        }
        var staffStack = new StackPanel { Spacing = 1 };
        for (int r = 0; r < staffModel.Rows.Count; r++)
        {
            var row = staffModel.Rows[r];
            var rGrid = new Grid { ColumnDefinitions = MainWindow.CreateClubListColumns(staffModel.Columns), Margin = new Thickness(4, 0, 16, 0) };
            for (int i = 0; i < row.Cells.Count; i++)
            {
                var txt = new TextBlock
                {
                    Text = row.Cells[i].Text,
                    FontSize = 11.5,
                    Foreground = UiKit.TextPrimary,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                };
                Grid.SetColumn(txt, i);
                rGrid.Children.Add(txt);
            }
            var rowBorder = new Border
            {
                Height = 28,
                Background = r == 0 ? UiKit.Brush("#2E1065") : (r % 2 == 1 ? UiKit.Brush("#131622") : UiKit.Brush("#151926")),
                Padding = new Thickness(8, 2),
                CornerRadius = new CornerRadius(3),
                Child = rGrid,
            };
            staffStack.Children.Add(rowBorder);
        }
        var staffTableCard = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Background = UiKit.SurfaceHeader, BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 6, 24, 6), Child = staffHeader },
                    new Border { Background = UiKit.Brush("#151926"), Padding = new Thickness(0), Child = staffStack }
                }
            }
        };

        var staffHost = new StackPanel { Spacing = 10, Width = 820, Background = UiKit.WindowBg, Children = { staffTableCard } };
        SaveControlToPng(staffHost, 850, 480, "review_club_staff.png");
        });
    }
}
