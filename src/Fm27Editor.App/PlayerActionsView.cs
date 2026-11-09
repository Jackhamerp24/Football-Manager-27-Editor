using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly Dictionary<ulong, Dictionary<PlayerField, (long Expected, long Value)>> playerScalarDrafts = [];

    void HookPlayerActions(PlayerRecord player)
    {
        var tab = playerTabs.FirstOrDefault(t => ReferenceEquals(t.Panel, editor));
        if (tab is not null) tab.Sticky.Content = CreatePlayerScalarsPanel(player);

        // 2. The actions panel is docked beside the section host by RenderEditor.

        UpdateScalarDraftState(player);
    }

    Control? CreatePlayerScalarsPanel(PlayerRecord player)
    {
        if (backend.State.Status != ConnectionStatus.Connected) return null;
        var fields = backend.Session.PlayerFields.ReadAll(player);
        if (fields.Count == 0) return null;

        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
        };

        bool hasAny = false;
        var existingDraft = playerScalarDrafts.GetValueOrDefault(player.Address);

        foreach (var d in SquadScalarDefinitions.Descriptors)
        {
            if (d.Field is not (PlayerField.Condition or PlayerField.MatchSharpness or PlayerField.Jadedness)) continue;
            if (!fields.TryGetValue(d.Field, out long snapshotVal))
                continue;

            hasAny = true;
            long currentVal = existingDraft?.TryGetValue(d.Field, out var staged) == true
                ? staged.Value
                : snapshotVal;

            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var lbl = new TextBlock
            {
                Text = d.Label,
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(lbl, $"{d.Label} ({d.Min} .. {d.Max})");

            var num = new NumericUpDown
            {
                Minimum = d.Min,
                Maximum = d.Max,
                Value = (decimal)currentVal,
                Width = 68,
                MinWidth = 0,
                Height = 22,
                MinHeight = 22,
                ShowButtonSpinner = false,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
            };

            num.ValueChanged += (_, _) =>
            {
                if (updating) return;
                if (num.Value is { } nv)
                {
                    long newL = (long)nv;
                    StageScalarChange(player, d.Field, snapshotVal, newL);
                }
            };

            item.Children.Add(lbl);
            item.Children.Add(num);
            panel.Children.Add(item);
        }

        return hasAny
            ? new Border
            {
                Background = UiKit.SurfaceAlt,
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 5),
                Child = panel,
            }
            : null;
    }

    void StageScalarChange(PlayerRecord player, PlayerField field, long snapshotVal, long newVal)
    {
        if (newVal == snapshotVal)
        {
            if (playerScalarDrafts.TryGetValue(player.Address, out var d))
            {
                d.Remove(field);
                if (d.Count == 0) playerScalarDrafts.Remove(player.Address);
            }
        }
        else
        {
            if (!playerScalarDrafts.TryGetValue(player.Address, out var d))
                playerScalarDrafts[player.Address] = d = [];
            d[field] = (snapshotVal, newVal);
        }

        UpdateScalarDraftState(player);
    }

    void UpdateScalarDraftState(PlayerRecord player)
    {
        int scalarCount = playerScalarDrafts.TryGetValue(player.Address, out var d) ? d.Count : 0;
        int stdCount = drafts.TryGetValue(player.Address, out var sd) ? sd.Count : 0;
        int total = scalarCount + stdCount + (informationDirty?.Invoke() == true ? 1 : 0);

        if (saveButton is not null) saveButton.IsEnabled = total > 0;
        if (discardButton is not null) discardButton.IsEnabled = total > 0;
        status.Text = total > 0 ? $"{total} unsaved change(s)" : "";
    }

    void HookScalarDraftButtons(PlayerRecord player)
    {
        UpdateScalarDraftState(player);

        if (saveButton is not null)
        {
            saveButton.Click += (_, _) =>
            {
                if (playerScalarDrafts.TryGetValue(player.Address, out var d) && d.Count > 0)
                {
                    int saved = 0;
                    foreach (var (f, (expected, val)) in d.ToArray())
                    {
                        try
                        {
                            backend.Edits.Execute("Player", player.DisplayName, "Page save", () => backend.Session.PlayerFields.Set(player, f, expected, val));
                            d.Remove(f);
                            saved++;
                        }
                        catch (Exception ex)
                        {
                            status.Text = $"Failed to save {f}: {ex.Message}";
                            return;
                        }
                    }
                    if (d.Count == 0)
                        playerScalarDrafts.Remove(player.Address);
                    status.Text = $"Saved {saved} change(s). Continue in FM to see updates.";
                    RenderEditor();
                }
            };
        }

        if (discardButton is not null)
        {
            discardButton.Click += (_, _) =>
            {
                if (playerScalarDrafts.Remove(player.Address))
                {
                    status.Text = "Changes discarded";
                    RenderEditor();
                }
            };
        }
    }

    void RegisterPlayerLabels(PlayerRecord p) =>
        backend.Edits.RegisterLabels("Player", p.DisplayName, Fm27Editor.Core.History.PlayerChangeLabels.For(backend.Session.Profile, p.Address));

    Control CreatePlayerActionsPanel(
        PlayerRecord? player,
        bool isSquadMode,
        Func<IReadOnlyList<PlayerRecord>>? getSquadPlayers,
        Action<string>? onStatus,
        Func<IReadOnlyList<PlayerRecord>>? getSelectedPlayers = null,
        Action? onApplied = null,
        Action<Action>? registerSelectionUpdater = null,
        bool allowGoldenGeneration = false)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(8, 8),
        };

        static Grid Pair(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }

        static TextBlock Subhead(string text, bool first = false) => new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = UiKit.Brush("#7E8BA3"),
            LetterSpacing = 0.4,
            Margin = new Thickness(1, first ? 2 : 6, 0, 1),
        };

        if (!isSquadMode && player is not null)
        {
            if (backend.Freezer is not null)
            {
                var freeze = new Button
                {
                    Content = "Freeze",
                    FontSize = 12.5,
                    Height = 24,
                    MinHeight = 24,
                    Padding = new Thickness(3, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                freeze.Click += (_, _) =>
                {
                    try
                    {
                        backend.Edits.Execute(() => backend.Freezer.SetPlayer(player, FrozenPlayer.AllAttributes(player)));
                        onStatus?.Invoke($"Frozen: {player.DisplayName}");
                    }
                    catch (Exception ex) { onStatus?.Invoke($"Freezer: {ex.Message}"); }
                };

                var unfreeze = new Button
                {
                    Content = "Unfreeze",
                    FontSize = 12.5,
                    Height = 24,
                    MinHeight = 24,
                    Padding = new Thickness(3, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                unfreeze.Click += (_, _) =>
                {
                    try
                    {
                        if (player.Id is { } pid)
                        {
                            backend.Edits.Execute(() => backend.Freezer.RemovePlayer(pid));
                            onStatus?.Invoke($"Unfrozen: {player.DisplayName}");
                        }
                    }
                    catch (Exception ex) { onStatus?.Invoke($"Freezer: {ex.Message}"); }
                };

                panel.Children.Add(Pair(freeze, unfreeze));
            }
            var export = new Button { Content = "Export", FontSize = 12.5, Height = 24, MinHeight = 24, Padding = new Thickness(3, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(export, "Export attributes");
            export.Click += async (_, _) => await ExportPlayerAttributesAsync(player);
            var import = new Button { Content = "Import", FontSize = 12.5, Height = 24, MinHeight = 24, Padding = new Thickness(3, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(import, "Import attributes");
            import.Click += async (_, _) => await ImportPlayerAttributesAsync(player);
            panel.Children.Add(Pair(export, import));
        }

        TextBlock? selectedCountText = null;
        if (isSquadMode)
        {
            // Every player in the table is included unless his box is unticked.
            selectedCountText = new TextBlock { FontSize = 12, Foreground = UiKit.Brush("#94A3B8"), Margin = new Thickness(1, -2, 0, 4) };
            void UpdateCount()
            {
                int included = getSelectedPlayers?.Invoke()?.Count ?? 0;
                selectedCountText.Text = included == 1 ? "( 1 Player )" : $"( {included} Players )";
            }
            UpdateCount();
            registerSelectionUpdater?.Invoke(UpdateCount);
            panel.Children.Add(selectedCountText);
        }
        panel.Children.Add(Subhead("CONTRACT", first: false));

        Button ActionBtn(string text, Action onClick, bool enabled = true)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 12.5,
                Height = 24,
                MinHeight = 24,
                Padding = new Thickness(3, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsEnabled = enabled,
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }

        void RunAction(string actionName, Action<PlayerRecord> action)
        {
            var selectedPlayers = isSquadMode ? getSelectedPlayers?.Invoke() ?? [] : [];
            bool batch = isSquadMode;
            if (batch)
            {
                var list = selectedPlayers;
                if (list.Count == 0)
                {
                    onStatus?.Invoke("No players selected");
                    return;
                }

                int success = 0;
                var failed = new List<string>();
                foreach (var p in list)
                {
                    try
                    {
                        { RegisterPlayerLabels(p); backend.Edits.Execute("Player", p.DisplayName, actionName, () => action(p)); }
                        success++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{p.DisplayName}: {ex.Message}");
                    }
                }

                if (failed.Count == 0)
                    onStatus?.Invoke($"{actionName}: {success} player(s) updated. Continue in FM to see updates.");
                else
                    onStatus?.Invoke($"{actionName}: {success} succeeded, {failed.Count} failed ({string.Join(", ", failed.Take(2))}).");

                if (success > 0)
                    onApplied?.Invoke();
            }
            else
            {
                var target = selectedPlayers.Count == 1 ? selectedPlayers[0] : (player ?? getSelectedPlayers?.Invoke()?.FirstOrDefault());
                if (target is null)
                {
                    onStatus?.Invoke("Select a player first");
                    return;
                }

                try
                {
                    long before = backend.Edits.WriteCount;
                    { RegisterPlayerLabels(target); backend.Edits.Execute("Player", target.DisplayName, actionName, () => action(target)); }
                    onStatus?.Invoke(before >= 0 && backend.Edits.WriteCount == before
                        ? $"{actionName}: {target.DisplayName} already has these values, nothing was changed."
                        : $"{actionName} applied to {target.DisplayName}. Continue in FM to see updates.");
                    onApplied?.Invoke();
                }
                catch (Exception ex)
                {
                    onStatus?.Invoke($"Failed {actionName}: {ex.Message}");
                }
            }
        }

        panel.Children.Add(ActionBtn("Renew for 4 Years", () =>
            RunAction("Renew for 4 Years", p => SquadAdapter.RenewForYears(backend.Session, p, 4))));

        panel.Children.Add(ActionBtn("Make it Expire", () =>
            RunAction("Make it Expire", p => SquadAdapter.MakeExpire(backend.Session, p))));

        panel.Children.Add(Pair(
            ActionBtn("−1 Year", () => RunAction("−1 Year", p => SquadAdapter.ShiftExpiry(backend.Session, p, -1))),
            ActionBtn("+1 Year", () => RunAction("+1 Year", p => SquadAdapter.ShiftExpiry(backend.Session, p, 1)))));

        if (!isSquadMode && player is not null)
        {
            panel.Children.Add(ActionBtn("Release Player", () => _ = ShowReleasePlayerAsync(player), enabled: player.Contract is not null));
            if (releaseUndo.Count > 0)
                panel.Children.Add(ActionBtn("Undo last release", () => _ = UndoReleaseAsync(), enabled: true));
        }

        if (!isSquadMode && player is not null)
        {
            panel.Children.Add(Subhead("TEAM"));
            bool captain = backend.Session.Profile.Team?.Captain is not null;
            bool vice = backend.Session.Profile.Team?.ViceCaptain is not null;
            panel.Children.Add(ActionBtn("Set captain", () => _ = ShowSetCaptainAsync(player, CaptainRole.Captain), enabled: captain));
            panel.Children.Add(ActionBtn("Set vice captain", () => _ = ShowSetCaptainAsync(player, CaptainRole.ViceCaptain), enabled: vice));
            if (captainUndo.Count > 0)
                panel.Children.Add(ActionBtn("Undo captain change", () => _ = UndoCaptainAsync(), enabled: true));
        }

        panel.Children.Add(Subhead("STATE"));

        panel.Children.Add(Pair(
            ActionBtn("Heal", () => RunAction("Heal", p => backend.Session.Heal(p))),
            ActionBtn("Inspire", () => RunAction("Inspire", p => backend.Session.Inspire(p)))));

        panel.Children.Add(Pair(
            ActionBtn("Destroy", () => RunAction("Destroy", p => backend.Session.Destroy(p))),
            ActionBtn("No Bans", () => RunAction("Remove bans", p => backend.Session.RemoveMatchBans(p)))));

        if (isSquadMode)
        {
            panel.Children.Add(Pair(
                ActionBtn("Freeze", () => RunAction("Freeze", p =>
                {
                    if (backend.Freezer is { } freezer)
                        freezer.SetPlayer(p, FrozenPlayer.AllAttributes(p));
                })),
                ActionBtn("Unfreeze", () => RunAction("Unfreeze", p =>
                {
                    if (backend.Freezer is { } freezer && p.Id is > 0)
                        freezer.RemovePlayer(p.Id.Value);
                }))));
        }

        // FMRTE's Information-page presets; each returns false when the player already has what it adds.
        void RunPreset(string actionName, Func<PlayerRecord, bool> preset)
        {
            int unchanged = 0, failed = 0;
            RunAction(actionName, p => { try { if (!preset(p)) unchanged++; } catch { failed++; throw; } });
            if (unchanged > 0 && failed == 0)
                onStatus?.Invoke($"{actionName}: {unchanged} player(s) already had it or have nothing to change.");
        }
        panel.Children.Add(ActionBtn("Remove Int. Withdrawal", () => RunPreset("Remove Int. Withdrawal", p => backend.Session.RemoveInternationalWithdrawal(p) > 0)));
        panel.Children.Add(ActionBtn("Register for UEFA", () => RunPreset("Register for UEFA", p => backend.Session.RegisterForUefa(p))));
        panel.Children.Add(ActionBtn("Register for Domestic", () => RunPreset("Register for Domestic", p => backend.Session.RegisterForDomestic(p))));
        panel.Children.Add(ActionBtn("Trained at club", () => RunPreset("Trained at club", p => backend.Session.TrainedAtCurrentClub(p))));
        panel.Children.Add(ActionBtn("Trained at nation", () => RunPreset("Trained at nation", p => backend.Session.TrainedInCurrentNation(p))));
        panel.Children.Add(ActionBtn("Team Cohesion", () => RunPreset("Improve Team Cohesion", p => backend.Session.SetTeamCohesion(p, TeamCohesionWriter.Improve))));
        panel.Children.Add(ActionBtn("Tactical Understanding", () => RunAction("Tactical Understanding", p => backend.Session.TacticalUnderstanding(p))));
        panel.Children.Add(ActionBtn("All-in-one", () => RunAction("All-in-one", p => backend.Session.AllInOne(p))));
        if (allowGoldenGeneration)
        {
            var minPa = new NumericUpDown { Minimum = 1, Maximum = 200, Value = Fm27Editor.Core.Players.GoldenGenerationWriter.DefaultMinPa, Height = 24, MinHeight = 24, FontSize = 12.5, FormatString = "0" };
            var maxPa = new NumericUpDown { Minimum = 1, Maximum = 200, Value = Fm27Editor.Core.Players.GoldenGenerationWriter.DefaultMaxPa, Height = 24, MinHeight = 24, FontSize = 12.5, FormatString = "0" };
            ToolTip.SetTip(minPa, "Minimum PA");
            ToolTip.SetTip(maxPa, "Maximum PA");
            panel.Children.Add(Subhead("YOUTH"));
            panel.Children.Add(Pair(minPa, maxPa));
            panel.Children.Add(ActionBtn("Golden Generation", () => RunAction("Golden Generation", p =>
                backend.Session.GoldenGeneration(p, (int)(minPa.Value ?? 150), (int)(maxPa.Value ?? 180)))));
        }
        if (!isSquadMode && player is not null)
        {
            var retirement = new PlannedRetirements(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase);
            if (retirement.IsSupported)
                panel.Children.Add(ActionBtn("Prevent retirement", () => _ = ShowSquadPresetAsync([player], PlayerPreset.Fitness, false, false, false, false, false, true)));
        }

        var card = UiKit.Card(isSquadMode ? "Presets" : "Tools", panel);
        card.Width = isSquadMode ? 196 : 168;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }
}
