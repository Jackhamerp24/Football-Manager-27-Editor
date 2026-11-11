using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record SwapOption(PlayerRecord Player)
    {
        public override string ToString() => $"{Player.DisplayName}  ·  {Player.Club?.DisplayName ?? "Free agent"}";
    }

    /// <summary>Swap Player: pick another player and both go to the other's club and team for no fee.</summary>
    Control SwapPlayerPanel(PlayerRecord player)
    {
        var search = new TextBox { Watermark = "Search player or ID…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var list = new ListBox
        {
            Height = 220, HorizontalAlignment = HorizontalAlignment.Stretch, Background = UiKit.SurfaceInput,
            BorderBrush = UiKit.BorderControl, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
        };
        var preview = new TextBlock { FontSize = 13.5, Foreground = UiKit.TextPrimary, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var warning = new TextBlock { Foreground = Brush("#F59E0B"), FontSize = 13, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var apply = new Button { Content = "Confirm swap", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 28, Classes = { "accent" } };
        var all = Players();

        void Filter()
        {
            if (player.Gender == Fm27Editor.Core.Offsets.Gender.Women || player.Club?.Gender == Fm27Editor.Core.Offsets.Gender.Women)
            {
                list.ItemsSource = Array.Empty<SwapOption>();
                warning.Text = "Only male players and men's clubs are supported.";
                warning.IsVisible = true;
                return;
            }
            string q = (search.Text ?? "").Trim();
            var compare = CultureInfo.InvariantCulture.CompareInfo;
            list.ItemsSource = all
                .Where(p => p.Address != player.Address && p.Club is not null && p.Club.Address != player.Club?.Address
                    && p.Gender != Fm27Editor.Core.Offsets.Gender.Women && p.Club.Gender != Fm27Editor.Core.Offsets.Gender.Women)
                .Where(p => q.Length == 0 || compare.IndexOf(p.DisplayName, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0 || p.Id?.ToString(Inv) == q)
                .OrderByDescending(p => p.CurrentAbility).Take(200).Select(p => new SwapOption(p)).ToArray();
        }
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) =>
        {
            warning.IsVisible = false;
            if (list.SelectedItem is not SwapOption other) { preview.IsVisible = false; apply.IsEnabled = false; return; }
            preview.Text = $"{player.DisplayName}: {player.Club?.DisplayName} → {other.Player.Club?.DisplayName}\n{other.Player.DisplayName}: {other.Player.Club?.DisplayName} → {player.Club?.DisplayName}\nNo fee. Each goes to the team the other plays for.";
            preview.IsVisible = true;
            apply.IsEnabled = true;
        };
        apply.Click += (_, _) =>
        {
            if (list.SelectedItem is not SwapOption other) return;
            apply.IsEnabled = false;
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Swap", () => backend.Session.SwapPlayers(player, other.Player));
            }
            catch (Exception ex)
            {
                warning.Text = UserMessage.For(ex); warning.IsVisible = true; apply.IsEnabled = true;
                status.Text = $"Swap stopped: {UserMessage.For(ex)}";
                return;
            }
            clubCache = null;
            contractDrafts.Remove(player.Address); contractDrafts.Remove(other.Player.Address);
            foreach (var address in new[] { player.Address, other.Player.Address })
                if (backend.Players.Reload(address) is { } fresh) { fresh.GameDate = backend.GameDate; ReplaceRow(fresh); if (address == player.Address) selected = fresh; }
            RenderEditor();
            status.Text = $"Swapped {player.DisplayName} and {other.Player.DisplayName}. Club finances show after Continue in FM.";
        };
        Filter();

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12, 10), Children = { search, list, preview, warning, apply } };
        return UiKit.Card("Swap Player", body);
    }
}
