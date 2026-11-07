using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Layout;

namespace Fm27Editor.App;

static class PitchSurface
{
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
            CornerRadius = new CornerRadius(size / 2), Foreground = Brush("#0B1311"), FontSize = 11, FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        SetRating(dot, rating);
        ToolTip.SetTip(dot, position.Label);
        Avalonia.Automation.AutomationProperties.SetName(dot, position.Label);
        return dot;
    }

    public static void SetRating(Button dot, int rating)
    {
        dot.Background = Brush(rating <= 5 ? "#334155" : rating <= 10 ? "#F59E0B" : rating <= 15 ? "#EAB308" : "#84CC16");
        dot.Content = rating.ToString();
    }
    static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
}
