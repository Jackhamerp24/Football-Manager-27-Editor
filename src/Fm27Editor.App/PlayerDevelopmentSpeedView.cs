using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Presets;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record SpeedOption(int Multiplier, string Label) { public override string ToString() => Label; }

    static readonly SpeedOption[] SpeedOptions =
        [new(0, "Normal speed"), .. AcceleratedDevelopment.OfferedMultipliers.Select(m => new SpeedOption(m, $"{m}x faster"))];

    /// <summary>
    /// "Accelerate Player Development": a speed for the player (or the ticked squad players) that multiplies his training
    /// gains while the game runs. Writes at once, like the other presets.
    /// </summary>
    void AddDevelopmentSpeed(StackPanel panel, PlayerRecord? player, bool isSquadMode, Func<IReadOnlyList<PlayerRecord>>? getSelectedPlayers,
        Action<string>? onStatus, Action? onApplied, Func<string, Action, bool, Button> actionButton)
    {
        var session = backend.Session;
        if (session is null || !session.Process.CanWrite) return;
        var accel = session.AcceleratedDevelopment;
        var speed = new ComboBox
        {
            ItemsSource = SpeedOptions, PlaceholderText = "Development speed", Height = 24, MinHeight = 24, FontSize = 12.5,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, MaxDropDownHeight = 260,
        };
        Button? set = null;
        set = actionButton("Set speed", () => _ = SetSpeedAsync(), false);
        speed.SelectionChanged += (_, _) => set.IsEnabled = speed.SelectedItem is SpeedOption;

        panel.Children.Add(new TextBlock
        {
            Text = "DEVELOPMENT", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#7E8BA3"),
            LetterSpacing = 0.4, Margin = new Thickness(1, 6, 0, 1),
        });
        panel.Children.Add(speed);
        panel.Children.Add(set);

        // Show the player's current speed once the code site is known; finding it reads the module, so not on the UI thread.
        if (!isSquadMode && player is not null)
            _ = Task.Run(() => { try { return accel.MultiplierOf(player); } catch { return -1; } }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (t.Result > 0 && speed.SelectedItem is null) speed.SelectedItem = SpeedOptions.FirstOrDefault(o => o.Multiplier == t.Result);
            }));

        async Task SetSpeedAsync()
        {
            if (speed.SelectedItem is not SpeedOption option) return;
            var targets = isSquadMode ? getSelectedPlayers?.Invoke() ?? [] : player is null ? [] : [player];
            if (targets.Count == 0) { onStatus?.Invoke("No players selected"); return; }
            set.IsEnabled = false;
            try
            {
                if (option.Multiplier > 0) await Task.Run(() => accel.Install());
                int changed = 0;
                var failed = new List<string>();
                foreach (var p in targets)
                {
                    try
                    {
                        bool did = false;
                        backend.Edits.Execute("Player", p.DisplayName, "Development speed", () => did = accel.Set(p, option.Multiplier));
                        if (did) changed++;
                    }
                    catch (Exception ex) { failed.Add($"{p.DisplayName}: {ex.Message}"); }
                }
                onApplied?.Invoke();
                string who = targets.Count == 1 ? targets[0].DisplayName : $"{changed} player(s)";
                onStatus?.Invoke(failed.Count > 0
                    ? $"Development speed: {changed} set, {failed.Count} failed ({string.Join(", ", failed.Take(2))})."
                    : changed == 0
                        ? $"Development speed: already {option.Label.ToLowerInvariant()}."
                        : $"Development speed {option.Label.ToLowerInvariant()} for {who}. Continue in FM to see updates.");
            }
            catch (Exception ex) { onStatus?.Invoke($"Development speed: {UserMessage.For(ex)}"); }
            finally { set.IsEnabled = speed.SelectedItem is SpeedOption; }
        }
    }
}
