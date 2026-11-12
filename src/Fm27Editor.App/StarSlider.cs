using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// A 0-5 rating slider in half steps, drawn as a track with a tick at every whole star and a round handle. Click or drag
/// to set it, arrow keys step by half a star; hovering shows the current value.
/// </summary>
internal sealed class StarSlider : Control
{
    const double Max = 5, Step = 0.5, Handle = 7;
    double value;
    bool dragging;

    public StarSlider(double initial)
    {
        value = Snap(initial);
        Height = 22;
        MinWidth = 160;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        UpdateTip();
    }

    public double Value
    {
        get => value;
        set
        {
            double v = Snap(value);
            if (v == this.value) return;
            this.value = v;
            UpdateTip();
            InvalidateVisual();
            ValueChanged?.Invoke(v);
        }
    }

    public event Action<double>? ValueChanged;

    static double Snap(double v) => Math.Clamp(Math.Round(v / Step) * Step, 0, Max);

    void UpdateTip() => ToolTip.SetTip(this, value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

    double Left => Handle;
    double Width0 => Math.Max(1, Bounds.Width - 2 * Handle);
    double X(double v) => Left + Width0 * v / Max;

    void SetFrom(Point p) => Value = (p.X - Left) / Width0 * Max;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragging = true;
        e.Pointer.Capture(this);
        Focus();
        SetFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragging) { SetFrom(e.GetPosition(this)); ToolTip.SetIsOpen(this, true); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Down) { Value -= Step; e.Handled = true; }
        else if (e.Key is Key.Right or Key.Up) { Value += Step; e.Handled = true; }
        else if (e.Key == Key.Home) { Value = 0; e.Handled = true; }
        else if (e.Key == Key.End) { Value = Max; e.Handled = true; }
    }

    public override void Render(DrawingContext context)
    {
        double mid = Bounds.Height / 2;
        var fill = MainWindow.RatingColor((int)Math.Round(value * 4));
        var track = new Rect(Left, mid - 3, Width0, 6);
        context.DrawRectangle(UiKit.Brush("#1E293B"), null, track, 3, 3);
        if (value > 0) context.DrawRectangle(fill, null, new Rect(Left, mid - 3, X(value) - Left, 6), 3, 3);
        var tick = new Pen(UiKit.Brush("#0B0F17"), 2);
        for (int star = 1; star < Max; star++)
            context.DrawLine(tick, new Point(X(star), mid - 3), new Point(X(star), mid + 3));
        var ring = new Pen(IsFocused ? UiKit.Brush("#F1F5F9") : UiKit.Brush("#0B0F17"), 2);
        context.DrawEllipse(fill, ring, new Point(X(value), mid), Handle, Handle);
    }

    protected override void OnGotFocus(GotFocusEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }
}
