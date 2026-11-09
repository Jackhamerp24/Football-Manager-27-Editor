using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Layout;

namespace Fm27Editor.App;

static class PitchSurface
{
    public static IBrush PositionColor(int value) =>
        Brush(value <= 0 ? "#1E293B" : value <= 5 ? "#334155" : value <= 10 ? "#F59E0B" : value <= 15 ? "#EAB308" : "#84CC16");

    public static IBrush PositionFg(int value) =>
        Brush(value <= 0 ? "#475569" : value <= 5 ? "#94A3B8" : value <= 10 ? "#F59E0B" : value <= 15 ? "#EAB308" : "#84CC16");

    public static IBrush PositionBg(int value) =>
        Brush(value <= 0 ? "#0A0D14" : value <= 5 ? "#0E1119" : value <= 10 ? "#1B1309" : value <= 15 ? "#1B1809" : "#0E1B12");

    public static IBrush PositionBorder(int value) =>
        Brush(value <= 0 ? "#1E2433" : value <= 5 ? "#23293B" : value <= 10 ? "#3D2912" : value <= 15 ? "#3D3712" : "#1D3B24");

    public static Canvas Create(double width, double height)
    {
        var pitch = new Canvas { Width = width, Height = height, Background = Brush("#0F382F"), ClipToBounds = true };
        var lines = Brush("#4F7E72");
        void Rectangle(double x, double y, double w, double h)
        {
            var shape = new Rectangle { Width = w, Height = h, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
            Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); pitch.Children.Add(shape);
        }
        Rectangle(1, 1, width - 2, height - 2);
        Rectangle(width * .26, 0, width * .48, height * .105);
        Rectangle(width * .37, 0, width * .26, height * .035);
        Rectangle(width * .26, height * .895, width * .48, height * .105);
        Rectangle(width * .37, height * .965, width * .26, height * .035);
        pitch.Children.Add(new Line { StartPoint = new Point(0, height / 2), EndPoint = new Point(width, height / 2), Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false });
        var circle = new Ellipse { Width = width * .2, Height = width * .2, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
        Canvas.SetLeft(circle, width * .4); Canvas.SetTop(circle, height / 2 - width * .1); pitch.Children.Add(circle);
        return pitch;
    }

    public static Button CreateDot(PitchPosition position, int rating)
    {
        const double size = 26;
        var dot = new Button { Width = size, Height = size, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            CornerRadius = new CornerRadius(size / 2), Foreground = Brush("#0B1311"), FontSize = 12.5, FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        SetRating(dot, rating);
        ToolTip.SetTip(dot, position.Label);
        Avalonia.Automation.AutomationProperties.SetName(dot, position.Label);
        return dot;
    }

    public static void SetRating(Button dot, int rating)
    {
        dot.Background = PositionColor(rating);
        dot.Foreground = Brush(rating <= 5 ? (rating <= 0 ? "#475569" : "#94A3B8") : "#0B1311");
        dot.Content = rating <= 0 ? "—" : rating.ToString();
    }

    public static Canvas CreateHorizontal(double width, double height)
    {
        var pitch = new Canvas { Width = width, Height = height, Background = Brush("#14532D"), ClipToBounds = true };
        var lines = Brush("#A7F3D0");
        const double pad = 10;
        double pw = width - pad * 2;
        double ph = height - pad * 2;

        int stripes = 8;
        double stripeW = pw / stripes;
        var darkStripe = Brush("#0F4224");
        for (int i = 0; i < stripes; i += 2)
        {
            var stripe = new Rectangle { Width = stripeW, Height = ph, Fill = darkStripe, IsHitTestVisible = false };
            Canvas.SetLeft(stripe, pad + i * stripeW); Canvas.SetTop(stripe, pad);
            pitch.Children.Add(stripe);
        }

        void Rectangle(double x, double y, double w, double h)
        {
            var shape = new Rectangle { Width = w, Height = h, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
            Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); pitch.Children.Add(shape);
        }

        Rectangle(pad, pad, pw, ph);

        pitch.Children.Add(new Line { StartPoint = new Point(width / 2, pad), EndPoint = new Point(width / 2, height - pad), Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false });

        double cr = ph * 0.36;
        var circle = new Ellipse { Width = cr, Height = cr, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
        Canvas.SetLeft(circle, width / 2 - cr / 2); Canvas.SetTop(circle, height / 2 - cr / 2); pitch.Children.Add(circle);

        var spot = new Ellipse { Width = 4, Height = 4, Fill = lines, IsHitTestVisible = false };
        Canvas.SetLeft(spot, width / 2 - 2); Canvas.SetTop(spot, height / 2 - 2); pitch.Children.Add(spot);

        double boxH = ph * 0.54;
        double boxW = pw * 0.16;
        Rectangle(pad, height / 2 - boxH / 2, boxW, boxH);

        double sixH = ph * 0.28;
        double sixW = pw * 0.06;
        Rectangle(pad, height / 2 - sixH / 2, sixW, sixH);

        Rectangle(width - pad - boxW, height / 2 - boxH / 2, boxW, boxH);

        Rectangle(width - pad - sixW, height / 2 - sixH / 2, sixW, sixH);

        var pSpotL = new Ellipse { Width = 3, Height = 3, Fill = lines, IsHitTestVisible = false };
        Canvas.SetLeft(pSpotL, pad + boxW * 0.65); Canvas.SetTop(pSpotL, height / 2 - 1.5); pitch.Children.Add(pSpotL);

        var pSpotR = new Ellipse { Width = 3, Height = 3, Fill = lines, IsHitTestVisible = false };
        Canvas.SetLeft(pSpotR, width - pad - boxW * 0.65 - 3); Canvas.SetTop(pSpotR, height / 2 - 1.5); pitch.Children.Add(pSpotR);

        double goalH = ph * 0.20;
        double goalD = 5;
        var goalL = new Rectangle { Width = goalD, Height = goalH, Stroke = lines, StrokeThickness = 1, IsHitTestVisible = false };
        Canvas.SetLeft(goalL, pad - goalD); Canvas.SetTop(goalL, height / 2 - goalH / 2); pitch.Children.Add(goalL);
        var goalR = new Rectangle { Width = goalD, Height = goalH, Stroke = lines, StrokeThickness = 1, IsHitTestVisible = false };
        Canvas.SetLeft(goalR, width - pad); Canvas.SetTop(goalR, height / 2 - goalH / 2); pitch.Children.Add(goalR);

        return pitch;
    }
    static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));   // the pitch is a drawing: it keeps its own colours in both themes
}
