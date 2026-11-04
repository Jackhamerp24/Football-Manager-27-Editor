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
        // 1. Direct editing row for PlayerFields (Condition, Sharpness, Jadedness, Morale, Asking Price, etc.)
        var scalarBar = CreatePlayerScalarsPanel(player);
        if (scalarBar is not null)
        {
            int insertIdx = editor.Children.Count > 0 && editor.Children[^1] is TabControl
                ? editor.Children.Count - 1
                : editor.Children.Count;
            editor.Children.Insert(insertIdx, scalarBar);
        }

        // 2. Dock Actions panel to the right of tabs
        if (editor.Children.Count > 0 && editor.Children[^1] is TabControl tabs)
        {
            editor.Children.RemoveAt(editor.Children.Count - 1);
            var host = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };
            tabs.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(tabs, 0);
            host.Children.Add(tabs);

            var actions = CreatePlayerActionsPanel(
                player: player,
                isSquadMode: false,
                getSquadPlayers: null,
                onStatus: msg => status.Text = msg);
            Grid.SetColumn(actions, 1);
            host.Children.Add(actions);

            editor.Children.Add(host);
        }

        // 3. Hook save and discard buttons for scalar drafts
        HookScalarDraftButtons(player);
    }

    Control? CreatePlayerScalarsPanel(PlayerRecord player)
    {
        if (backend.State.Status != ConnectionStatus.Connected) return null;
        var fields = backend.Session.PlayerFields.ReadAll(player);
        if (fields.Count == 0) return null;

        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
        };

        bool hasAny = false;
        var existingDraft = playerScalarDrafts.GetValueOrDefault(player.Address);

        foreach (var d in SquadScalarDefinitions.Descriptors)
        {
            if (!fields.TryGetValue(d.Field, out long snapshotVal))
                continue;

            hasAny = true;
            long currentVal = existingDraft?.TryGetValue(d.Field, out var staged) == true
                ? staged.Value
                : snapshotVal;

            var box = new Border
            {
                Background = Brush("#151824"),
                BorderBrush = Brush("#252A3D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5),
                Margin = new Thickness(0, 0, 8, 4),
                Width = d.Width,
            };

            var stack = new StackPanel { Spacing = 3 };
            var lbl = new TextBlock
            {
                Text = d.Label,
                FontSize = 10.5,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#94A3B8"),
            };
            ToolTip.SetTip(lbl, $"{d.Label} ({d.Min} .. {d.Max})");

            var num = new NumericUpDown
            {
                Minimum = d.Min,
                Maximum = d.Max,
                Value = (decimal)currentVal,
                Height = 26,
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

            stack.Children.Add(lbl);
            stack.Children.Add(num);
            box.Child = stack;
            panel.Children.Add(box);
        }

        return hasAny ? panel : null;
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
        int total = scalarCount + stdCount;

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
        Action<string>? onStatus)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            Width = 200,
        };

        var title = new TextBlock
        {
            Text = "ACTIONS",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#94A3B8"),
            Margin = new Thickness(0, 2, 0, 4),
        };
        panel.Children.Add(title);

        CheckBox? applyToAll = null;
        if (isSquadMode)
        {
            int squadCount = getSquadPlayers?.Invoke()?.Count ?? 0;
            string applyText = squadCount > 0 ? $"Apply to all ({squadCount})" : "Apply to all";
            applyToAll = new CheckBox
            {
                Content = applyText,
                IsChecked = false,
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            ToolTip.SetTip(applyToAll, $"Apply to all = every player of the selected team ({squadCount})");
            panel.Children.Add(applyToAll);
        }

        // Section: CONTRACT
        panel.Children.Add(new TextBlock
        {
            Text = "CONTRACT",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#7C3AED"),
            Margin = new Thickness(0, 4, 0, 2),
        });

        Button ActionBtn(string text, Action onClick, bool enabled = true)
        {
            var btn = new Button
            {
                Content = text,
                Height = 26,
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
            bool toAll = applyToAll?.IsChecked == true;
            if (toAll)
            {
                var list = getSquadPlayers?.Invoke() ?? [];
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

        if (!isSquadMode && player is not null)
        {
            panel.Children.Add(ActionBtn("Release Player", () => _ = ShowReleasePlayerAsync(player), enabled: player.Contract is not null));
            panel.Children.Add(ActionBtn("Undo last release", () => _ = UndoReleaseAsync(), enabled: releaseUndo.Count > 0));
        }

        // Section: TEAM
        panel.Children.Add(new TextBlock
        {
            Text = "TEAM",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 8, 0, 2),
        });
        if (!isSquadMode && player is not null)
        {
            bool captain = backend.Session.Profile.Team?.Captain is not null;
            bool vice = backend.Session.Profile.Team?.ViceCaptain is not null;
            panel.Children.Add(ActionBtn("Set captain", () => _ = ShowSetCaptainAsync(player, CaptainRole.Captain), enabled: captain));
            panel.Children.Add(ActionBtn("Set vice captain", () => _ = ShowSetCaptainAsync(player, CaptainRole.ViceCaptain), enabled: vice));
            panel.Children.Add(ActionBtn("Undo captain change", () => _ = UndoCaptainAsync(), enabled: captainUndo.Count > 0));
        }

        panel.Children.Add(ActionBtn("−1 Year", () =>
            RunAction("−1 Year", p => SquadAdapter.ShiftExpiry(backend.Session, p, -1))));

        panel.Children.Add(ActionBtn("+1 Year", () =>
            RunAction("+1 Year", p => SquadAdapter.ShiftExpiry(backend.Session, p, 1))));

        // Section: STATE
        panel.Children.Add(new TextBlock
        {
            Text = "STATE",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 8, 0, 2),
        });

        // Heal -> session.Heal(player)
        panel.Children.Add(ActionBtn("Heal", () =>
            RunAction("Heal", p => backend.Session.Heal(p))));

        // Inspire -> session.Inspire(player)
        panel.Children.Add(ActionBtn("Inspire", () =>
            RunAction("Inspire", p => backend.Session.Inspire(p))));

        // Destroy -> session.Destroy(player)
        panel.Children.Add(ActionBtn("Destroy", () =>
            RunAction("Destroy", p => backend.Session.Destroy(p))));

        // Remove bans -> session.RemoveMatchBans(player)
        panel.Children.Add(ActionBtn("Remove bans", () =>
        {
            bool toAll = applyToAll?.IsChecked == true;
            if (toAll)
            {
                var list = getSquadPlayers?.Invoke() ?? [];
                if (list.Count == 0)
                {
                    onStatus?.Invoke("No players in team");
                    return;
                }

                int totalRemoved = 0;
                int affected = 0;
                var failed = new List<string>();
                foreach (var p in list)
                {
                    try
                    {
                        backend.Edits.Execute(() =>
                        {
                            int count = backend.Session.RemoveMatchBans(p);
                            if (count > 0)
                            {
                                totalRemoved += count;
                                affected++;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{p.DisplayName}: {ex.Message}");
                    }
                }

                if (failed.Count == 0)
                    onStatus?.Invoke($"Remove bans: removed {totalRemoved} match ban(s) across {affected} player(s). Continue in FM to see updates.");
                else
                    onStatus?.Invoke($"Remove bans: removed {totalRemoved} across {affected} player(s), {failed.Count} failed ({string.Join(", ", failed.Take(2))}).");
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
                    int removed = 0;
                    backend.Edits.Execute(() =>
                    {
                        removed = backend.Session.RemoveMatchBans(player);
                    });
                    onStatus?.Invoke($"Remove bans: removed {removed} match ban(s) from {player.DisplayName}. Continue in FM to see updates.");
                }
                catch (Exception ex)
                {
                    onStatus?.Invoke($"Failed Remove bans: {ex.Message}");
                }
            }
        }));

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
        panel.Children.Add(ActionBtn("Improve Team Cohesion", () => RunPreset("Improve Team Cohesion", p => backend.Session.SetTeamCohesion(p, TeamCohesionWriter.Improve))));
        panel.Children.Add(ActionBtn("Destroy Team Cohesion", () => RunPreset("Destroy Team Cohesion", p => backend.Session.SetTeamCohesion(p, TeamCohesionWriter.Destroy))));
        panel.Children.Add(ActionBtn("All-in-one", () => RunAction("All-in-one", p => backend.Session.AllInOne(p))));
        if (!isSquadMode && player is not null)
        {
            var retirement = new PlannedRetirements(backend.Session.Memory, backend.Session.Profile, backend.Session.ModuleBase);
            panel.Children.Add(ActionBtn("Prevent retirement", () => _ = ShowSquadPresetAsync([player], PlayerPreset.Fitness, false, false, false, false, false, true), enabled: retirement.IsSupported));
        }

        string[] otherStateActions =
        [
            "Set condition",
            "Set sharpness",
            "Set morale",
            "International withdrawal",
            "Tactical understanding"
        ];

        foreach (var act in otherStateActions)
        {
            panel.Children.Add(ActionBtn(act, () => { }, enabled: false));
        }

        return new Border
        {
            Background = Brush("#151926"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 10),
            VerticalAlignment = VerticalAlignment.Top,
            Child = panel,
        };
    }
}
