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
                FontSize = 11,
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
                            backend.Edits.Execute(() => backend.Session.PlayerFields.Set(player, f, expected, val));
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

    Control CreatePlayerActionsPanel(
        PlayerRecord? player,
        bool isSquadMode,
        Func<IReadOnlyList<PlayerRecord>>? getSquadPlayers,
        Action<string>? onStatus,
        Func<IReadOnlyList<PlayerRecord>>? getSelectedPlayers = null)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(5, 6),
        };

        static Grid Pair(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4 };
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(left);
            g.Children.Add(right);
            return g;
        }

        static TextBlock Subhead(string text, bool first = false) => new()
        {
            Text = text,
            FontSize = 9.5,
            FontWeight = FontWeight.Bold,
            Foreground = UiKit.Brush("#7E8BA3"),
            LetterSpacing = 0.4,
            Margin = new Thickness(1, first ? 2 : 6, 0, 1),
        };

        if (!isSquadMode && player is not null)
        {
            if (backend.Freezer is not null)
            {
                var freeze = new Button { Content = "Freeze", FontSize = 11, Height = 24, MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
                freeze.Click += async (_, _) => await ShowPlayerFreezerAsync(player);
                panel.Children.Add(freeze);
            }
            var export = new Button { Content = "Export", FontSize = 11, Height = 24, MinHeight = 24, Padding = new Thickness(3, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(export, "Export attributes");
            export.Click += async (_, _) => await ExportPlayerAttributesAsync(player);
            var import = new Button { Content = "Import", FontSize = 11, Height = 24, MinHeight = 24, Padding = new Thickness(3, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(import, "Import attributes");
            import.Click += async (_, _) => await ImportPlayerAttributesAsync(player);
            panel.Children.Add(Pair(export, import));
        }

        CheckBox? applyToAll = null;
        if (isSquadMode)
        {
            int squadCount = getSquadPlayers?.Invoke()?.Count ?? 0;
            string applyText = squadCount > 0 ? $"Apply to all ({squadCount})" : "Apply to all";
            applyToAll = new CheckBox
            {
                Content = applyText,
                IsChecked = false,
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            };
            ToolTip.SetTip(applyToAll, $"Apply to all = every player of the selected team ({squadCount})");
            panel.Children.Add(applyToAll);
        }

        panel.Children.Add(Subhead("CONTRACT", first: isSquadMode && applyToAll is null));

        Button ActionBtn(string text, Action onClick, bool enabled = true)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 11,
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
            var selectedPlayers = isSquadMode && applyToAll?.IsChecked != true
                ? getSelectedPlayers?.Invoke() ?? [] : [];
            bool batch = applyToAll?.IsChecked == true || selectedPlayers.Count > 1;
            if (batch)
            {
                var list = applyToAll?.IsChecked == true ? getSquadPlayers?.Invoke() ?? [] : selectedPlayers;
                if (list.Count == 0)
                {
                    onStatus?.Invoke("No players in team");
                    return;
                }

                int success = 0;
                var failed = new List<string>();
                foreach (var p in list)
                {
                    try
                    {
                        backend.Edits.Execute(() => action(p));
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
            }
            else
            {
                if (player is null)
                {
                    onStatus?.Invoke("Select a player first");
                    return;
                }

                try
                {
                    backend.Edits.Execute(() => action(player));
                    onStatus?.Invoke($"{actionName} applied to {player.DisplayName}. Continue in FM to see updates.");
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

        panel.Children.Add(ActionBtn("Tactical Familiarity", () =>
            RunAction("Tactical Understanding", p =>
            {
                var access = backend.Session.PlayerFamiliarity;
                var snapshot = access.Read(p.Address) ?? throw new StaleObjectException("Tactical understanding is unavailable");
                access.SetTactical(snapshot, PlayerFamiliarity.MaxTactical);
            })));

        // FMRTE's Information-page presets; each returns false when the player already has what it adds.
        void RunPreset(string actionName, Func<PlayerRecord, bool> preset)
        {
            int unchanged = 0, failed = 0;
            RunAction(actionName, p => { try { if (!preset(p)) unchanged++; } catch { failed++; throw; } });
            if (unchanged > 0 && failed == 0)
                onStatus?.Invoke($"{actionName}: {unchanged} player(s) already had it or have nothing to change.");
        }
        panel.Children.Add(ActionBtn("Register for UEFA", () => RunPreset("Register for UEFA", p => backend.Session.RegisterForUefa(p))));
        panel.Children.Add(ActionBtn("Trained at club", () => RunPreset("Trained at club", p => backend.Session.TrainedAtCurrentClub(p))));
        panel.Children.Add(ActionBtn("Trained at nation", () => RunPreset("Trained at nation", p => backend.Session.TrainedInCurrentNation(p))));
        panel.Children.Add(ActionBtn("Team Cohesion", () => RunPreset("Improve Team Cohesion", p => backend.Session.SetTeamCohesion(p, TeamCohesionWriter.Improve))));
        panel.Children.Add(ActionBtn("All-in-one", () => RunAction("All-in-one", p => backend.Session.AllInOne(p))));
        if (!isSquadMode && player is not null)
        {
            var retirement = new PlannedRetirements(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase);
            if (retirement.IsSupported)
                panel.Children.Add(ActionBtn("Prevent retirement", () => _ = ShowSquadPresetAsync([player], PlayerPreset.Fitness, false, false, false, false, false, true)));
        }

        var card = UiKit.Card("Tools", panel);
        card.Width = isSquadMode ? 152 : 132;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }
}
