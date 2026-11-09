using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

public partial class MainWindow
{
    Control ColourInput(TextBox box) => ColourInputControl.Create(box);
}

/// <summary>A colour text box with a swatch button that opens the colour picker; used wherever a colour is edited.</summary>
static class ColourInputControl
{
    public static Control Create(TextBox box)
    {
        var button = new Button
        {
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = UiKit.Brush("#3A3F55"),
            BorderThickness = new Thickness(1),
        };
        void UpdateSwatch()
        {
            var text = (box.Text ?? "").Trim().TrimStart('#');
            if (text.Length == 6) text = "FF" + text;
            if (text.Length == 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                button.Background = new SolidColorBrush(Color.FromUInt32(argb));
        }
        box.TextChanged += (_, _) => UpdateSwatch();
        UpdateSwatch();
        button.Click += async (_, _) =>
        {
            string text = (box.Text ?? "").Trim().TrimStart('#');
            bool rgbOnly = text.Length == 6;
            if (text.Length == 6) text = "FF" + text;
            uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint initial);
            var picker = new ColourPickerWindow(initial);
            var result = await picker.ShowDialog<uint?>((Window)TopLevel.GetTopLevel(button)!);
            if (result is { } value) box.Text = rgbOnly ? $"#{value & 0xFFFFFF:X6}" : $"#{value:X8}";
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { button, box } };
    }
}

sealed class ColourPickerWindow : Window
{
    const double SpectrumW = 312, SpectrumH = 164;
    double hue, saturation, value, alpha;
    readonly Border spectrum = new() { Width = SpectrumW, Height = SpectrumH, ClipToBounds = true, BorderBrush = UiKit.Brush("#2B3147"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
    readonly Ellipse cursorOuter = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    readonly Ellipse cursorInner = new() { Width = 8, Height = 8, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false };
    readonly Border alphaRamp = new() { Height = 6, Width = SpectrumW, CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
    readonly Border newPreview = new() { Width = 54, Height = 26, CornerRadius = new CornerRadius(3, 0, 0, 3) };
    readonly Border oldPreview = new() { Width = 54, Height = 26, CornerRadius = new CornerRadius(0, 3, 3, 0) };
    readonly TextBox hex = new() { Width = 108, HorizontalContentAlignment = HorizontalAlignment.Left };
    readonly Slider hueSlider = new() { Minimum = 0, Maximum = 360, Width = SpectrumW };
    readonly Slider alphaSlider = new() { Minimum = 0, Maximum = 255, Width = SpectrumW };
    readonly ComboBox mode = new() { Width = 78, ItemsSource = new[] { "RGB", "HSV", "CMYK" }, SelectedIndex = 0 };
    readonly TextBlock[] channelLabels = [new() { FontSize = 11.5, Foreground = UiKit.Brush("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center }, new() { FontSize = 11.5, Foreground = UiKit.Brush("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center }, new() { FontSize = 11.5, Foreground = UiKit.Brush("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center }, new() { FontSize = 11.5, Foreground = UiKit.Brush("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center }];
    readonly TextBox[] channels = [new() { Width = 52, HorizontalContentAlignment = HorizontalAlignment.Right }, new() { Width = 52, HorizontalContentAlignment = HorizontalAlignment.Right }, new() { Width = 52, HorizontalContentAlignment = HorizontalAlignment.Right }, new() { Width = 52, HorizontalContentAlignment = HorizontalAlignment.Right }];
    readonly StackPanel[] channelColumns = new StackPanel[4];
    bool updating;

    public ColourPickerWindow(uint initial)
    {
        Title = "Pick a colour";
        Width = 348;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var c = Color.FromUInt32(initial);
        oldPreview.Background = new SolidColorBrush(c);
        alpha = c.A;
        double r = c.R / 255d, g = c.G / 255d, b = c.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        value = max;
        saturation = max == 0 ? 0 : delta / max;
        hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta + 6) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        hueSlider.Value = hue;
        alphaSlider.Value = alpha;

        var dark = new Border { Background = Gradient(Colors.Transparent, Colors.Black, false), IsHitTestVisible = false };
        var markerCanvas = new Canvas { IsHitTestVisible = false, Children = { cursorOuter, cursorInner } };
        spectrum.Child = new Panel { Children = { dark, markerCanvas } };

        void Move(PointerEventArgs e)
        {
            if (!e.GetCurrentPoint(spectrum).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(spectrum);
            double w = spectrum.Bounds.Width > 0 ? spectrum.Bounds.Width : SpectrumW;
            double h = spectrum.Bounds.Height > 0 ? spectrum.Bounds.Height : SpectrumH;
            saturation = Math.Clamp(p.X / w, 0, 1);
            value = 1 - Math.Clamp(p.Y / h, 0, 1);
            Refresh();
        }
        spectrum.PointerPressed += (_, e) => { e.Pointer.Capture(spectrum); Move(e); };
        spectrum.PointerMoved += (_, e) => Move(e);
        spectrum.PointerReleased += (_, e) => e.Pointer.Capture(null);
        hueSlider.ValueChanged += (_, _) => { if (!updating) { hue = hueSlider.Value; Refresh(); } };
        alphaSlider.ValueChanged += (_, _) => { if (!updating) { alpha = alphaSlider.Value; Refresh(); } };
        mode.SelectionChanged += (_, _) => { if (!updating) Refresh(); };
        foreach (var channel in channels) channel.LostFocus += (_, _) => ReadChannels();
        hex.LostFocus += (_, _) => ParseHex();

        var hueRamp = new Border
        {
            Height = 6,
            Width = SpectrumW,
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(255, 0, 0), 0.00),
                    new GradientStop(Color.FromRgb(255, 255, 0), 0.17),
                    new GradientStop(Color.FromRgb(0, 255, 0), 0.33),
                    new GradientStop(Color.FromRgb(0, 255, 255), 0.50),
                    new GradientStop(Color.FromRgb(0, 0, 255), 0.67),
                    new GradientStop(Color.FromRgb(255, 0, 255), 0.83),
                    new GradientStop(Color.FromRgb(255, 0, 0), 1.00),
                },
            },
        };

        var previewFrame = new Border
        {
            BorderBrush = UiKit.Brush("#2B3147"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Background = UiKit.Brush("#1A1E2E"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { newPreview, oldPreview } },
        };
        ToolTip.SetTip(newPreview, "Selected colour");
        ToolTip.SetTip(oldPreview, "Current colour");

        var topHexRow = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(previewFrame, Dock.Left);
        topHexRow.Children.Add(previewFrame);
        var hexGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "Hex", Foreground = UiKit.Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center }, hex } };
        DockPanel.SetDock(hexGroup, Dock.Right);
        topHexRow.Children.Add(hexGroup);

        var channelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Bottom };
        mode.VerticalAlignment = VerticalAlignment.Bottom;
        channelRow.Children.Add(mode);
        for (int i = 0; i < 4; i++)
        {
            channelColumns[i] = new StackPanel { Spacing = 2, Children = { channelLabels[i], channels[i] } };
            channelRow.Children.Add(channelColumns[i]);
        }

        var ok = new Button { Content = "OK", Classes = { "accent" }, MinWidth = 68, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => { ParseHexIfFocused(); ReadChannelsIfFocused(); Close(ToColour().ToUInt32()); };
        var cancel = new Button { Content = "Cancel", MinWidth = 68, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
            else if (e.Key == Key.Enter && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
            {
                Close(ToColour().ToUInt32());
                e.Handled = true;
            }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                spectrum,
                new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "Hue", FontSize = 12.5, Foreground = UiKit.Brush("#94A3B8") }, hueRamp, hueSlider } },
                new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "Alpha", FontSize = 12.5, Foreground = UiKit.Brush("#94A3B8") }, alphaRamp, alphaSlider } },
                topHexRow,
                channelRow,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { cancel, ok } },
            },
        };
        Refresh();
    }

    void ParseHexIfFocused()
    {
        if (hex.IsFocused) ParseHex();
    }

    void ReadChannelsIfFocused()
    {
        if (Array.Exists(channels, c => c.IsFocused)) ReadChannels();
    }

    void ParseHex()
    {
        string raw = (hex.Text ?? "").Trim().TrimStart('#');
        if (raw.Length == 6) raw = "FF" + raw;
        if (raw.Length != 8 || !uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed)) { Refresh(); return; }
        var colour = Color.FromUInt32(parsed);
        alpha = colour.A;
        double rr = colour.R / 255d, gg = colour.G / 255d, bb = colour.B / 255d;
        double hi = Math.Max(rr, Math.Max(gg, bb)), lo = Math.Min(rr, Math.Min(gg, bb)), d = hi - lo;
        value = hi;
        saturation = hi == 0 ? 0 : d / hi;
        hue = d == 0 ? 0 : hi == rr ? 60 * (((gg - bb) / d + 6) % 6) : hi == gg ? 60 * ((bb - rr) / d + 2) : 60 * ((rr - gg) / d + 4);
        Refresh();
    }

    static LinearGradientBrush Gradient(Color from, Color to, bool horizontal) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(horizontal ? 1 : 0, horizontal ? 0 : 1, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(from, 0), new(to, 1) }
    };

    Color ToColour(double? s = null, double? v = null)
    {
        double ss = s ?? saturation, vv = v ?? value, chroma = vv * ss;
        double x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1)), m = vv - chroma;
        var (r, g, b) = hue < 60 ? (chroma, x, 0d) : hue < 120 ? (x, chroma, 0d) : hue < 180 ? (0d, chroma, x) : hue < 240 ? (0d, x, chroma) : hue < 300 ? (x, 0d, chroma) : (chroma, 0d, x);
        return Color.FromArgb((byte)Math.Round(alpha), (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    void Refresh()
    {
        updating = true;
        hueSlider.Value = hue;
        alphaSlider.Value = alpha;
        var pure = ToColour(1, 1);
        spectrum.Background = Gradient(Colors.White, Color.FromRgb(pure.R, pure.G, pure.B), true);
        var colour = ToColour();
        newPreview.Background = new SolidColorBrush(colour);
        alphaRamp.Background = Gradient(Color.FromArgb(0, colour.R, colour.G, colour.B), Color.FromArgb(255, colour.R, colour.G, colour.B), true);

        double w = spectrum.Bounds.Width > 0 ? spectrum.Bounds.Width : SpectrumW;
        double h = spectrum.Bounds.Height > 0 ? spectrum.Bounds.Height : SpectrumH;
        double cx = saturation * w;
        double cy = (1 - value) * h;
        Canvas.SetLeft(cursorOuter, cx - 6);
        Canvas.SetTop(cursorOuter, cy - 6);
        Canvas.SetLeft(cursorInner, cx - 4);
        Canvas.SetTop(cursorInner, cy - 4);

        hex.Text = $"#{colour.ToUInt32():X8}";
        int[] values;
        string[] names;
        if (mode.SelectedIndex == 1)
        {
            values = [(int)Math.Round(hue), (int)Math.Round(saturation * 100), (int)Math.Round(value * 100)];
            names = ["H", "S%", "V%"];
        }
        else if (mode.SelectedIndex == 2)
        {
            double r = colour.R / 255d, g = colour.G / 255d, b = colour.B / 255d;
            double k = 1 - Math.Max(r, Math.Max(g, b));
            double scale = 1 - k;
            values = [(int)Math.Round((scale == 0 ? 0 : (1 - r - k) / scale) * 100), (int)Math.Round((scale == 0 ? 0 : (1 - g - k) / scale) * 100), (int)Math.Round((scale == 0 ? 0 : (1 - b - k) / scale) * 100), (int)Math.Round(k * 100)];
            names = ["C", "M", "Y", "K"];
        }
        else
        {
            values = [colour.R, colour.G, colour.B];
            names = ["R", "G", "B"];
        }
        for (int i = 0; i < channels.Length; i++)
        {
            bool vis = i < values.Length;
            channelColumns[i].IsVisible = vis;
            if (vis)
            {
                channelLabels[i].Text = names[i];
                channels[i].Text = values[i].ToString(CultureInfo.InvariantCulture);
            }
        }
        updating = false;
    }

    void ReadChannels()
    {
        if (updating) return;
        int count = mode.SelectedIndex == 2 ? 4 : 3;
        var n = new double[count];
        for (int i = 0; i < count; i++)
            if (!double.TryParse(channels[i].Text, NumberStyles.Number, CultureInfo.InvariantCulture, out n[i])) { Refresh(); return; }
        if (mode.SelectedIndex == 1)
        {
            hue = Math.Clamp(n[0], 0, 360);
            saturation = Math.Clamp(n[1], 0, 100) / 100;
            value = Math.Clamp(n[2], 0, 100) / 100;
        }
        else
        {
            double r, g, b;
            if (mode.SelectedIndex == 2)
            {
                double k = Math.Clamp(n[3], 0, 100) / 100;
                r = (1 - Math.Clamp(n[0], 0, 100) / 100) * (1 - k);
                g = (1 - Math.Clamp(n[1], 0, 100) / 100) * (1 - k);
                b = (1 - Math.Clamp(n[2], 0, 100) / 100) * (1 - k);
            }
            else
            {
                r = Math.Clamp(n[0], 0, 255) / 255;
                g = Math.Clamp(n[1], 0, 255) / 255;
                b = Math.Clamp(n[2], 0, 255) / 255;
            }
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            value = max;
            saturation = max == 0 ? 0 : d / max;
            hue = d == 0 ? 0 : max == r ? 60 * (((g - b) / d + 6) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        }
        Refresh();
    }
}
