using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    Grid? loadingOverlay;
    TextBlock? loadingHeading;
    TextBlock? loadingDetail;
    DispatcherTimer? loadingDots;
    int loadingDotCount;

    /// <summary>
    /// The in-page loading screen: "Loading game data" with a moving bar and the step being read. It covers the content
    /// while Load Game warms the lists, so a blank page never looks like a frozen editor.
    /// </summary>
    Control BuildLoadingOverlay()
    {
        loadingHeading = new TextBlock { Text = "Loading game data", FontSize = 16, FontWeight = FontWeight.SemiBold };
        loadingDetail = new TextBlock
        {
            Text = "Connecting to the game…", FontSize = 12.5, Foreground = UiKit.TextSecondary,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 420,
        };
        var bar = new ProgressBar
        {
            IsIndeterminate = true, Height = 6, MinHeight = 6, Margin = new Thickness(0, 12, 0, 0),
            Foreground = UiKit.Accent, Background = UiKit.BorderSubtle,
        };
        var card = new Border
        {
            Width = 460, Padding = new Thickness(22, 18, 22, 22),
            Background = UiKit.Surface, BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel { Spacing = 3, Children = { loadingHeading, loadingDetail, bar } },
        };
        loadingOverlay = new Grid
        {
            IsVisible = false,
            Children =
            {
                new Border { Background = UiKit.WindowBg, Opacity = 0.94 },
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Children = { card },
                },
            },
        };
        loadingDots = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        loadingDots.Tick += (_, _) =>
        {
            loadingDotCount = (loadingDotCount + 1) % 4;
            if (loadingHeading is not null) loadingHeading.Text = "Loading game data" + new string('.', loadingDotCount);
        };
        return loadingOverlay;
    }

    void ShowLoading(string detail)
    {
        if (loadingOverlay is null) return;
        loadingOverlay.IsVisible = true;
        SetLoadingDetail(detail);
        loadingDots?.Start();
    }

    /// <summary>Safe from the reading threads: the text is posted to the UI thread.</summary>
    void SetLoadingDetail(string detail)
    {
        if (loadingDetail is null) return;
        if (Dispatcher.UIThread.CheckAccess()) loadingDetail.Text = detail;
        else Dispatcher.UIThread.Post(() => { if (loadingDetail is not null) loadingDetail.Text = detail; });
    }

    void HideLoading()
    {
        loadingDots?.Stop();
        loadingDotCount = 0;
        if (loadingHeading is not null) loadingHeading.Text = "Loading game data";
        if (loadingOverlay is not null) loadingOverlay.IsVisible = false;
    }
}
