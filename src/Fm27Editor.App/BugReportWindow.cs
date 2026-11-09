using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Diagnostics;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    const string BugIcon = "M20,8H17.19C16.74,7.22 16.12,6.55 15.37,6.04L17,4.41L15.59,3L13.42,5.17C12.96,5.06 12.5,5 12,5C11.5,5 11.04,5.06 10.59,5.17L8.41,3L7,4.41L8.63,6.04C7.88,6.55 7.26,7.22 6.81,8H4V10H6.09C6.04,10.33 6,10.66 6,11V12H4V14H6V15C6,15.34 6.04,15.67 6.09,16H4V18H6.81C7.85,19.79 9.78,21 12,21C14.22,21 16.15,19.79 17.19,18H20V16H17.91C17.96,15.67 18,15.34 18,15V14H20V12H18V11C18,10.66 17.96,10.33 17.91,10H20V8M14,16H10V14H14V16M14,12H10V10H14V12Z";

    Window? bugWindow;

    /// <summary>Bottom-right of the status bar, after the game version, so it never covers the version text.</summary>
    Button BuildBugButton()
    {
        var button = new Button
        {
            Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(BugIcon), Fill = Brush("#94A3B8"), Stretch = Stretch.Uniform, Width = 14, Height = 14 },
            Padding = new Thickness(0), MinWidth = 24, Width = 24, MinHeight = 20, Height = 20,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, "Report a problem");
        button.Click += (_, _) => ShowBugReport();
        return button;
    }

    /// <summary>What the editor and game look like right now, for a report made while the editor is still open.</summary>
    List<string> BugReportState()
    {
        var lines = new List<string>
        {
            $"Connection: {backend.State.Status} - {backend.State.Message}",
            $"Save changed in game since the editor read it: {backend.SaveChanged}",
            $"Status bar: {status.Text}",
        };
        try { lines.Add($"Game date: {(backend.State.Status == ConnectionStatus.Connected ? backend.GameDate?.ToString("yyyy-MM-dd") : "not connected")}"); }
        catch (Exception ex) { lines.Add("Game date: unreadable (" + ex.GetType().Name + ")"); }
        return lines;
    }

    List<string> BugReportEdits()
    {
        var edits = new List<string>();
        try
        {
            foreach (var g in backend.Edits?.Journal?.Groups ?? [])
                edits.Add($"{g.Time:HH:mm:ss} {g.Kind} / {g.Entity}: {g.Title} ({g.Entries.Count} writes{(g.FullyUndone ? ", undone" : "")})");
        }
        catch (Exception) { }
        return edits;
    }

    void ShowBugReport()
    {
        if (bugWindow is { } open) { open.Activate(); return; }
        var description = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, Width = 440, VerticalContentAlignment = VerticalAlignment.Top,
            Watermark = "What happened? What were you doing?",
        };
        var result = new TextBlock { Foreground = UiKit.TextSecondary, TextWrapping = TextWrapping.Wrap, Width = 440 };
        var create = new Button { Content = "Create report", Classes = { "accent" } };
        var openFolder = new Button { Content = "Open folder", IsVisible = false };
        string? folder = null;
        create.Click += async (_, _) =>
        {
            if (FlightRecorder.Current is not { } recorder) { result.Text = "No log is available for this run."; return; }
            create.IsEnabled = false;
            try
            {
                string text = description.Text ?? "";
                var state = BugReportState();
                var edits = BugReportEdits();
                string target = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "dev";
                string zip = await Task.Run(() => BugReport.Create(recorder, target, text, version, state, edits));
                folder = target;
                result.Text = zip;
                openFolder.IsVisible = true;
            }
            catch (Exception ex) { result.Text = UserMessage.For(ex); }
            finally { create.IsEnabled = true; }
        };
        openFolder.Click += (_, _) =>
        {
            if (folder is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { create, openFolder } };
        var window = new Window
        {
            Title = "Report a problem", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = UiKit.WindowBg,
            Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { description, buttons, result } },
        };
        bugWindow = window;
        window.Closed += (_, _) => bugWindow = null;
        window.Show(this);
    }
}
