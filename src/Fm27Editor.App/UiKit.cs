using System.Globalization;
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
    /// <summary>True when the event came from a row of a list. A double-tap on a header, a scroll bar or empty space must not open the selected item.</summary>
    public static bool OnRow(Avalonia.Interactivity.RoutedEventArgs e) =>
        e.Source is Avalonia.Visual v && (v is DataGridRow || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<DataGridRow>(v) is not null
            || v is ListBoxItem || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ListBoxItem>(v) is not null);

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


    /// <summary>
    /// A local option group (Home/Away kits, contract kinds and the like) in the app's own language: the same text buttons and
    /// 2px accent underline as the module row on the main screen, never the system TabControl.
    /// </summary>
    public static Control OptionTabs(IReadOnlyList<(string Title, Control Content)> pages, int selectedIndex = 0, Action<int>? onSelected = null)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 8) };
        var host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        var buttons = new List<Button>();
        void Apply(int index)
        {
            index = Math.Clamp(index, 0, Math.Max(0, pages.Count - 1));
            for (int i = 0; i < buttons.Count; i++)
            {
                bool active = i == index;
                buttons[i].Foreground = active ? TextPrimary : TextMuted;
                buttons[i].FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
                buttons[i].Background = active ? Brush("#201B38") : Brushes.Transparent;
                buttons[i].BorderBrush = active ? Accent : Brushes.Transparent;
            }
            host.Content = pages.Count == 0 ? null : pages[index].Content;
        }
        for (int i = 0; i < pages.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = pages[i].Title,
                FontSize = 13,
                Padding = new Thickness(11, 3),
                MinHeight = 25,
                Height = 25,
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = Brushes.Transparent,
                Foreground = TextMuted,
            };
            button.Click += (_, _) => { Apply(index); onSelected?.Invoke(index); };
            buttons.Add(button);
            bar.Children.Add(button);
        }
        Apply(selectedIndex);
        return new StackPanel { Children = { bar, host } };
    }

    public static IBrush Brush(string hex) => AppTheme.Brush(hex);

    /// <summary>
    /// The small "i" next to a title whose effect is not obvious (Force Accept, Wonderkid Academy …): resting the mouse on
    /// it shows <paramref name="explanation"/>. Use it for behaviour a player could not guess, not for every field.
    /// </summary>
    public static Border InfoBadge(string explanation)
    {
        var badge = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
            Background = SurfaceInput, BorderBrush = BorderSubtle, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Child = new TextBlock
            {
                Text = "i", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTip.SetTip(badge, new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
        ToolTip.SetShowDelay(badge, 150);
        return badge;
    }

    /// <summary>A title followed by its <see cref="InfoBadge"/>.</summary>
    public static StackPanel TitleWithInfo(TextBlock title, string explanation)
    {
        // The title's own margin moves to the row, so the badge centres on the text.
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = title.Margin };
        title.Margin = default;
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        row.Children.Add(InfoBadge(explanation));
        return row;
    }

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
            FontSize = 12.5,
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
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = highlight ? Brush("#E9D5FF") : TextSecondary,
        }
    };

    public static TextBlock MetaText(string text, bool highlight = false) => new()
    {
        Text = text,
        FontSize = 13.5,
        FontWeight = highlight ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = highlight ? Brush("#C084FC") : TextMuted,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock MetaDot() => new()
    {
        Text = "·",
        FontSize = 13.5,
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
        FontSize = 13.5,
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
            FontSize = 13,
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
            FontSize = 12.5,
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

    /// <summary>
    /// Reusable OOP builder for high-density two-column form cards (like Contract Details, Loan Details).
    /// Standardizes column widths (160,*,24,160,*), typography, field alignments, and spacing.
    /// </summary>
    public sealed class TwoColumnFormBuilder
    {
        readonly Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("160,*,24,160,*"), RowSpacing = 6 };
        readonly int[] rowOf = [0, 0];

        public Grid Grid => grid;

        public TwoColumnFormBuilder AddRow(int column, string label, Control? control, Control? extra = null)
        {
            if (control is null) return this;
            int colIndex = column == 0 ? 0 : 1;
            int row = rowOf[colIndex]++;
            while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var name = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("#CBD5E1")
            };
            Grid.SetRow(name, row);
            Grid.SetColumn(name, colIndex == 0 ? 0 : 3);
            grid.Children.Add(name);

            // Fill the value column so both columns line up; a content-sized box used to centre itself in the column and
            // made the rows look ragged.
            control.Width = double.NaN;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Control value = control;
            if (extra is not null)
            {
                var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64"), ColumnSpacing = 6 };
                Grid.SetColumn(extra, 1);
                pair.Children.Add(control);
                pair.Children.Add(extra);
                value = pair;
            }
            value.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(value, row);
            Grid.SetColumn(value, colIndex == 0 ? 1 : 4);
            grid.Children.Add(value);
            return this;
        }

        public Border BuildCard(string title)
        {
            grid.Margin = new Thickness(12, 10);
            return Card(title, grid);
        }
    }

    /// <summary>
    /// Reusable OOP card for club financial statements (Summary, Income, Expenditure) matching FMRTE layout.
    /// </summary>
    public static class FinancialStatementCard
    {
        public static Border Create(
            string title,
            IReadOnlyList<string> periodHeaders,
            IEnumerable<(string Label, IReadOnlyList<long> Values, bool Bold)> rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("280,140,140,140"), RowSpacing = 1, Margin = new Thickness(0, 4) };
            int headerRow = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i <= periodHeaders.Count; i++)
            {
                var head = new TextBlock
                {
                    Text = i == 0 ? "Item" : $"{periodHeaders[i - 1]} (£)",
                    FontSize = 12.5,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brush("#94A3B8"),
                    Margin = new Thickness(i == 0 ? 8 : 0, 0, i == 0 ? 0 : 12, 4),
                    HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                };
                Grid.SetRow(head, headerRow);
                Grid.SetColumn(head, i);
                grid.Children.Add(head);
            }

            foreach (var (label, values, bold) in rows)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var name = new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = Brush(bold ? "#F8FAFC" : "#CBD5E1"),
                };
                Grid.SetRow(name, row);
                Grid.SetColumn(name, 0);
                grid.Children.Add(name);

                for (int i = 0; i < values.Count && i < periodHeaders.Count; i++)
                {
                    long val = values[i];
                    var value = new TextBlock
                    {
                        Text = val.ToString("N0", CultureInfo.InvariantCulture),
                        FontSize = 13,
                        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                        TextAlignment = TextAlignment.Right,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 0, 12, 0),
                        Foreground = Brush(val < 0 ? "#F87171" : "#F8FAFC"),
                    };
                    Grid.SetRow(value, row);
                    Grid.SetColumn(value, i + 1);
                    grid.Children.Add(value);
                }
            }
            return Card(title, grid);
        }
    }
}
