using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// The one way a date is entered anywhere in the editor: a text box that takes typed text in the chosen date layout (30-06-2027 by default, also 30/06/2027)
/// and has a calendar button inside its right edge. It is still a <see cref="TextBox"/>, so every screen that reads its text
/// keeps working; picking a day, or leaving the box with a date typed in another layout, rewrites the text to <see cref="Format"/>.
/// </summary>
public sealed class DateBox : TextBox
{
    const string CalendarIcon = "M3,4H5V2H7V4H17V2H19V4H21A1,1 0 0,1 22,5V20A1,1 0 0,1 21,21H3A1,1 0 0,1 2,20V5A1,1 0 0,1 3,4M4,9V19H20V9H4Z";

    readonly Avalonia.Controls.Calendar calendar = new() { DisplayDateStart = new DateTime(1900, 1, 1), DisplayDateEnd = new DateTime(2300, 12, 31) };
    readonly Flyout flyout = new();
    bool syncing;

    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <summary>How the text is written after a day is picked or a typed date is understood.</summary>
    public string Format { get => formatOverride ?? DateSetting.Pattern; set => formatOverride = value; }
    string? formatOverride;

    /// <summary>Runs when a day was picked or typed text was understood; the text already holds the day in <see cref="Format"/>.</summary>
    public event Action<DateOnly>? Committed;

    /// <summary>Reads typed text in any accepted layout; false for text that is not a date.</summary>
    public static bool TryParse(string? text, out DateOnly date) =>
        DateSetting.TryParse(text, out date);

    public DateBox()
    {
        flyout.Content = calendar;
        InnerRightContent = new Button
        {
            Width = 22, MinWidth = 22, Height = 20, MinHeight = 20, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Flyout = flyout,
            Content = new PathIcon { Width = 11, Height = 11, Foreground = UiKit.Brush("#94A3B8"), Data = Geometry.Parse(CalendarIcon) },
        };
        flyout.Opening += (_, _) =>
        {
            syncing = true;
            if (TryParse(Text, out var day)) { calendar.SelectedDate = day.ToDateTime(TimeOnly.MinValue); calendar.DisplayDate = calendar.SelectedDate.Value; }
            else { calendar.SelectedDate = null; calendar.DisplayDate = DateTime.Today; }
            syncing = false;
        };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (syncing || calendar.SelectedDate is not { } picked) return;
            var day = DateOnly.FromDateTime(picked);
            Text = day.ToString(Format, CultureInfo.InvariantCulture);
            flyout.Hide();
            Committed?.Invoke(day);
        };
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (string.IsNullOrWhiteSpace(Text) || !TryParse(Text, out var day)) return;
        string normal = day.ToString(Format, CultureInfo.InvariantCulture);
        if (Text != normal) Text = normal;
        Committed?.Invoke(day);
    }
}
