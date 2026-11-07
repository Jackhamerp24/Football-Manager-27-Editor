using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// Shared visual primitives for Football Manager 27 Editor: restrained single-accent dark palette,
/// uniform card headers, entity banners, balanced two-column masonry grids, and vector kit preview.
/// </summary>
static class UiKit
{
    public static readonly IBrush WindowBg = Brush("#0C0E14");
    public static readonly IBrush Surface = Brush("#131620");
    public static readonly IBrush SurfaceAlt = Brush("#10131C");
    public static readonly IBrush SurfaceHeader = Brush("#181C28");
    public static readonly IBrush SurfaceZebra = Brush("#161A26");
    public static readonly IBrush SurfaceInput = Brush("#0E1119");
    public static readonly IBrush BorderSubtle = Brush("#222738");
    public static readonly IBrush BorderControl = Brush("#2D344B");
    public static readonly IBrush Accent = Brush("#7C3AED");
    public static readonly IBrush AccentSoft = Brush("#241B42");
    public static readonly IBrush AccentBorder = Brush("#6D28D9");
    public static readonly IBrush TextPrimary = Brush("#F8FAFC");
    public static readonly IBrush TextSecondary = Brush("#CBD5E1");
    public static readonly IBrush TextMuted = Brush("#94A3B8");

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static Border Card(string title, Control body, Control? customTitle = null, Control? rightHeader = null)
    {
        var headerDock = new DockPanel { LastChildFill = true };
        if (rightHeader is not null)
        {
            DockPanel.SetDock(rightHeader, Dock.Right);
            headerDock.Children.Add(rightHeader);
        }

        var pip = new Border
        {
            Width = 3,
            Height = 12,
            CornerRadius = new CornerRadius(1.5),
            Background = Accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };
        DockPanel.SetDock(pip, Dock.Left);
        headerDock.Children.Add(pip);

        headerDock.Children.Add(customTitle ?? new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#E2E8F0"),
            VerticalAlignment = VerticalAlignment.Center,
            LetterSpacing = 0.3,
        });

        var header = new Border
        {
            Background = SurfaceHeader,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 30,
            Padding = new Thickness(10, 4),
            Child = headerDock,
        };

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(header);
        stack.Children.Add(body);

        return new Border
        {
            Background = Surface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = stack,
        };
    }

    public static Border MetaPill(string text, bool highlight = false) => new()
    {
        Height = 22,
        VerticalAlignment = VerticalAlignment.Center,
        Background = highlight ? AccentSoft : Brush("#191D2B"),
        BorderBrush = highlight ? AccentBorder : Brush("#282F45"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(7, 0),
        Margin = new Thickness(0, 0, 5, 0),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = highlight ? Brush("#E9D5FF") : TextSecondary,
        }
    };

    public static TextBlock MetaText(string text, bool highlight = false) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = highlight ? Brush("#C084FC") : TextMuted,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock MetaDot() => new()
    {
        Text = "·",
        FontSize = 12,
        FontWeight = FontWeight.Bold,
        Foreground = Brush("#475569"),
        Margin = new Thickness(6, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Button MetaLinkButton(object content, bool highlight = false) => new()
    {
        Content = content,
        Height = 22,
        MinHeight = 22,
        Padding = new Thickness(0),
        Margin = new Thickness(0),
        FontSize = 12,
        FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Normal,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = highlight ? Brush("#C084FC") : TextSecondary,
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Classes = { "meta-link" },
    };

    public static Border StatBox(string label, Control valueControl)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#A78BFA"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(valueControl);
        return new Border
        {
            Background = SurfaceInput,
            BorderBrush = Brush("#2E354D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 3, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
        };
    }

    public static Border EntityBanner(
        string title,
        Control? badgeRow,
        Control? rightCommands,
        params Control[] leadingVisuals)
    {
        var dock = new DockPanel { LastChildFill = true };
        if (rightCommands is not null)
        {
            rightCommands.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(rightCommands, Dock.Right);
            dock.Children.Add(rightCommands);
        }

        var leftRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var visual in leadingVisuals)
        {
            visual.VerticalAlignment = VerticalAlignment.Center;
            leftRow.Children.Add(visual);
        }

        var textStack = new StackPanel
        {
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = TextPrimary,
            LetterSpacing = -0.2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (badgeRow is not null)
            textStack.Children.Add(badgeRow);

        leftRow.Children.Add(textStack);
        dock.Children.Add(leftRow);

        return new Border
        {
            Background = Brush("#141824"),
            BorderBrush = Brush("#262C42"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0),
            MinHeight = 88,
            ClipToBounds = true,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("3,*"),
                Children =
                {
                    new Border { Background = Accent },
                    new Border { Padding = new Thickness(16, 12, 16, 12), Child = dock, [Grid.ColumnProperty] = 1 },
                },
            },
        };
    }

    public static Grid TwoColumnMasonry(IReadOnlyList<(Control Card, int Weight)> items, double gap = 10)
    {
        if (items.Count == 1)
        {
            var single = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = gap };
            Grid.SetColumn(items[0].Card, 0);
            single.Children.Add(items[0].Card);
            return single;
        }

        var left = new StackPanel { Spacing = gap, HorizontalAlignment = HorizontalAlignment.Stretch };
        var right = new StackPanel { Spacing = gap, HorizontalAlignment = HorizontalAlignment.Stretch };
        int leftWeight = 0, rightWeight = 0;

        foreach (var (card, weight) in items)
        {
            int w = Math.Max(2, weight);
            if (leftWeight <= rightWeight)
            {
                left.Children.Add(card);
                leftWeight += w;
            }
            else
            {
                right.Children.Add(card);
                rightWeight += w;
            }
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = gap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public sealed class KitPreviewHandle
    {
        public required Border Control { get; init; }
        public required Action<uint, uint, uint, uint, uint, uint?, uint?> Update { get; init; }
    }

    public static KitPreviewHandle CreateKitPreview(string caption)
    {
        var shirtBody = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 42,16 L 64,8 L 76,20 L 88,8 L 110,16 L 132,42 L 115,56 L 104,42 L 104,114 L 48,114 L 48,42 L 37,56 L 20,42 Z"),
            StrokeThickness = 1.6,
        };
        var shirtTrim = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 64,8 L 76,22 L 88,8 L 84,12 L 76,26 L 68,12 Z M 20,42 L 37,56 L 40,52 L 24,38 Z M 132,42 L 115,56 L 112,52 L 128,38 Z"),
        };
        var shirtStripe = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 70,23 L 82,23 L 82,113 L 70,113 Z"),
            Opacity = 0.22,
        };
        var numberShadow = new TextBlock
        {
            Text = "10",
            FontSize = 26,
            FontWeight = FontWeight.Black,
            Width = 56,
            TextAlignment = TextAlignment.Center,
        };
        Canvas.SetLeft(numberShadow, 49);
        Canvas.SetTop(numberShadow, 49);

        var numberText = new TextBlock
        {
            Text = "10",
            FontSize = 25,
            FontWeight = FontWeight.Black,
            Width = 56,
            TextAlignment = TextAlignment.Center,
        };
        Canvas.SetLeft(numberText, 48);
        Canvas.SetTop(numberText, 48);

        var shortsShape = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 49,122 L 103,122 L 109,158 L 80,158 L 76,142 L 72,158 L 43,158 Z"),
            StrokeThickness = 1.4,
        };
        var socksShape = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 53,166 L 68,166 L 68,198 L 47,198 L 47,190 L 53,190 Z M 84,166 L 99,166 L 99,190 L 105,190 L 105,198 L 84,198 Z"),
            StrokeThickness = 1.2,
        };

        var canvas = new Canvas
        {
            Width = 152,
            Height = 208,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { shirtBody, shirtStripe, shirtTrim, numberShadow, numberText, shortsShape, socksShape },
        };

        var label = new TextBlock
        {
            Text = caption,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var inner = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(12, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { label, canvas },
        };

        var card = Card("Kit Preview", inner);
        card.Width = 186;
        card.VerticalAlignment = VerticalAlignment.Top;

        static IBrush FromArgb(uint argb) => new SolidColorBrush(Color.FromUInt32(argb));

        void Update(uint shirtBg, uint shirtFg, uint shirtOutline, uint numFill, uint numOutline, uint? shortsBg, uint? socksBg)
        {
            shirtBody.Fill = FromArgb(shirtBg);
            shirtBody.Stroke = FromArgb(shirtOutline);
            shirtTrim.Fill = FromArgb(shirtFg);
            shirtStripe.Fill = FromArgb(shirtFg);
            numberText.Foreground = FromArgb(numFill);
            numberShadow.Foreground = FromArgb(numOutline);
            shortsShape.Fill = FromArgb(shortsBg ?? shirtFg);
            shortsShape.Stroke = FromArgb(shirtOutline);
            socksShape.Fill = FromArgb(socksBg ?? shirtBg);
            socksShape.Stroke = FromArgb(shirtOutline);
        }

        Update(0xFFC00020, 0xFFF8F8F8, 0xFF222738, 0xFFF8F8F8, 0xFF10131C, 0xFFF8F8F8, 0xFF151822);
        return new KitPreviewHandle { Control = card, Update = Update };
    }
}
