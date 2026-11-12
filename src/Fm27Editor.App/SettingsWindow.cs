using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    const string GearIcon = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z";

    Window? settingsWindow;

    /// <summary>The toolbar's settings button: opens the one Settings window.</summary>
    Button BuildSettingsButton()
    {
        var button = new Button
        {
            Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(GearIcon), Fill = Brush("#DCE2EE"), Stretch = Stretch.Uniform, Width = 16, Height = 16 },
            HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0),
            MinWidth = 34, Width = 34, MinHeight = 28, Height = 28,
            Background = Brush("#181C29"), BorderBrush = Brush("#272D42"), BorderThickness = new Thickness(1), Foreground = Brush("#DCE2EE"),
        };
        ToolTip.SetTip(button, "Settings");
        button.Click += (_, _) => ShowSettings();
        return button;
    }

    /// <summary>Settings: one window, shown once. The date format applies everywhere in the editor.</summary>
    void ShowSettings()
    {
        if (settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var example = new DateOnly(2027, 3, 30);
        var layouts = Enum.GetValues<DateLayout>();
        var choices = layouts.Select(l => new DateChoice(l, $"{DateSetting.HintOf(l)}    {example.ToString(DateSetting.PatternOf(l), Inv)}")).ToArray();
        var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.First(c => c.Layout == DateSetting.Layout), Width = 230 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not DateChoice chosen || chosen.Layout == DateSetting.Layout) return;
            DateSetting.Set(chosen.Layout);
            ReloadCurrentScreen();
            status.Text = $"Date format {DateSetting.Hint}";
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), ColumnSpacing = 12 };
        var label = new TextBlock { Text = "Date format", VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary };
        Grid.SetColumn(combo, 1);
        row.Children.Add(label); row.Children.Add(combo);

        // First entry follows the game's own currency (re-read at every Load Game); any other entry is the player's
        // choice and stays. Money is always stored in pounds; this only changes what is shown and how typed amounts convert.
        var detected = Fm27Editor.Core.Settings.GameCurrency.Detected();
        string autoLabel = detected is { } d ? $"Same as the game ({d.Code})" : "Same as the game (not recognised, £)";
        var currencyChoices = new[] { new CurrencyChoice(null, autoLabel) }
            .Concat(Fm27Editor.Core.Settings.CurrencySetting.Available.Select(c => new CurrencyChoice(c, $"{c.Code} - {c.Name} - {c.Symbol}"))).ToArray();
        var currencyCombo = new ComboBox
        {
            ItemsSource = currencyChoices,
            SelectedItem = Fm27Editor.Core.Settings.CurrencySetting.FollowGame
                ? currencyChoices[0]
                : currencyChoices.First(c => c.Info?.Code == Fm27Editor.Core.Settings.CurrencySetting.Current.Code),
            Width = 260,
        };
        currencyCombo.SelectionChanged += (_, _) =>
        {
            if (currencyCombo.SelectedItem is not CurrencyChoice chosen) return;
            if (chosen.Info is null)
            {
                Fm27Editor.Core.Settings.CurrencySetting.SetFollowGame();
                Fm27Editor.Core.Settings.GameCurrency.TryApply();
            }
            else Fm27Editor.Core.Settings.CurrencySetting.Set(chosen.Info);
            ReloadCurrentScreen();
            var now = Fm27Editor.Core.Settings.CurrencySetting.Current;
            status.Text = $"Currency {now.Code} - {now.Name}";
        };
        var currencyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), ColumnSpacing = 12 };
        var currencyLabel = new TextBlock { Text = "Currency", VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary };
        Grid.SetColumn(currencyCombo, 1);
        currencyRow.Children.Add(currencyLabel); currencyRow.Children.Add(currencyCombo);
        var window = new Window
        {
            Title = "Settings", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = UiKit.WindowBg,
            Content = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = { row, currencyRow } },
        };
        settingsWindow = window;
        window.Closed += (_, _) => settingsWindow = null;
        window.Show(this);
    }

    sealed record DateChoice(DateLayout Layout, string Text) { public override string ToString() => Text; }
    sealed record CurrencyChoice(Fm27Editor.Core.Settings.CurrencyInfo? Info, string Text) { public override string ToString() => Text; }
}
