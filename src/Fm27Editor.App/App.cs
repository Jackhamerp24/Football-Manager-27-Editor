using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Fm27Editor.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid")) { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
        InputElement.KeyDownEvent.AddClassHandler<NumericUpDown>((control, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
            TopLevel.GetTopLevel(control)?.FocusManager?.ClearFocus();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        InputElement.KeyDownEvent.AddClassHandler<TextBox>((control, e) =>
        {
            if (e.Handled || e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || control.AcceptsReturn) return;
            if (control.Watermark?.StartsWith("Search", StringComparison.OrdinalIgnoreCase) == true) return;
            TopLevel.GetTopLevel(control)?.FocusManager?.ClearFocus();
            e.Handled = true;
        }, RoutingStrategies.Bubble);
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<DataGridColumnHeader>((header, _) =>
        {
            static void CollapseSortReserve(DataGridColumnHeader h)
            {
                foreach (var g in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(h).OfType<Grid>())
                {
                    foreach (var col in g.ColumnDefinitions)
                        if (col.MinWidth >= 20) col.MinWidth = 0;
                }
            }
            CollapseSortReserve(header);
            header.Loaded += (_, _) => CollapseSortReserve(header);
        });
        ConfigureStyles();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    void ConfigureStyles()
    {
        var accent = new SolidColorBrush(Color.Parse("#7C3AED"));
        var accentHover = new SolidColorBrush(Color.Parse("#8B5CF6"));
        var accentSubtle = new SolidColorBrush(Color.Parse("#241B42"));
        var windowBg = new SolidColorBrush(Color.Parse("#0C0E14"));
        var surface = new SolidColorBrush(Color.Parse("#131620"));
        var surfaceInput = new SolidColorBrush(Color.Parse("#0E1119"));
        var surfaceElevated = new SolidColorBrush(Color.Parse("#191D2B"));
        var surfaceHover = new SolidColorBrush(Color.Parse("#22283B"));
        var surfaceDisabled = new SolidColorBrush(Color.Parse("#141722"));
        var borderSubtle = new SolidColorBrush(Color.Parse("#222738"));
        var borderControl = new SolidColorBrush(Color.Parse("#2D344B"));
        var borderDisabled = new SolidColorBrush(Color.Parse("#1E2333"));
        var textPrimary = new SolidColorBrush(Color.Parse("#F1F5F9"));
        var textSecondary = new SolidColorBrush(Color.Parse("#94A3B8"));
        var textDisabled = new SolidColorBrush(Color.Parse("#525B72"));

        Resources["SystemAccentColor"] = Color.Parse("#7C3AED");
        Resources["SystemAccentColorDark1"] = Color.Parse("#6D28D9");
        Resources["SystemAccentColorLight1"] = Color.Parse("#8B5CF6");
        Resources["DataGridRowSelectedBackgroundOpacity"] = 1.0;
        Resources["DataGridRowSelectedHoveredBackgroundOpacity"] = 1.0;
        Resources["DataGridRowSelectedUnfocusedBackgroundOpacity"] = 1.0;
        Resources["DataGridRowSelectedBackgroundBrush"] = new SolidColorBrush(Color.Parse("#231B42"));
        Resources["ListBoxItemSelectedBackgroundThemeBrush"] = new SolidColorBrush(Color.Parse("#231B42"));
        Resources["ListBoxItemSelectedPointerOverBackgroundThemeBrush"] = new SolidColorBrush(Color.Parse("#2B2150"));

        Styles.Add(new Style(x => x.Is<Window>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, windowBg),
                new Setter(TemplatedControl.ForegroundProperty, textPrimary),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
            }
        });

        Styles.Add(new Style(x => x.OfType<ScrollViewer>())
        {
            Setters =
            {
                new Setter(ScrollViewer.AllowAutoHideProperty, false),
            }
        });

        Styles.Add(new Style(x => x.OfType<ScrollBar>())
        {
            Setters =
            {
                new Setter(ScrollBar.AllowAutoHideProperty, false),
            }
        });

        Styles.Add(new Style(x => x.OfType<GridSplitter>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, borderSubtle),
            }
        });

        Styles.Add(new Style(x => x.OfType<GridSplitter>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, accent),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(Layoutable.HeightProperty, 24.0),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(TemplatedControl.BackgroundProperty, surfaceElevated),
                new Setter(TemplatedControl.BorderBrushProperty, borderControl),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.ForegroundProperty, textPrimary),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Stretch),
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, surfaceHover),
                new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(Color.Parse("#434C6B"))),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class(":disabled").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, surfaceDisabled),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, borderDisabled),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, textDisabled),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class("accent"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, accent),
                new Setter(TemplatedControl.BorderBrushProperty, accentHover),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class("accent").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, accentHover),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, accentHover),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, Brushes.White),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class("meta-link"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class("meta-link").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, Brushes.Transparent),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderThicknessProperty, new Thickness(0)),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.PaddingProperty, new Thickness(0)),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class("meta-link").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, Brushes.Transparent),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderThicknessProperty, new Thickness(0)),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, Brushes.White),
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class("chip"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, accentSubtle),
                new Setter(TemplatedControl.BorderBrushProperty, accent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.ForegroundProperty, textPrimary),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 0)),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
            }
        });

        Styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(Layoutable.HeightProperty, 24.0),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(7, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.BackgroundProperty, surfaceInput),
                new Setter(TemplatedControl.BorderBrushProperty, borderControl),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            }
        });

        Styles.Add(new Style(x => x.OfType<TextBox>().Template().OfType<Avalonia.Controls.Presenters.TextPresenter>())
        {
            Setters =
            {
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
            }
        });

        Styles.Add(new Style(x => x.OfType<TextBox>().PropertyEquals(TextBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Right).Template().OfType<Avalonia.Controls.Presenters.TextPresenter>())
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(0, 0, 3, 0)),
            }
        });

        Styles.Add(new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(Layoutable.HeightProperty, 24.0),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0, 0, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.BackgroundProperty, surfaceInput),
                new Setter(TemplatedControl.BorderBrushProperty, borderControl),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            }
        });

        Styles.Add(new Style(x => x.OfType<ComboBox>().Template().OfType<ContentControl>())
        {
            Setters =
            {
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            }
        });

        // NumericUpDown: Fluent's ButtonSpinner renders the visible frame, so style non-borderless NumericUpDown + ButtonSpinner
        // identically to TextBox/ComboBox, while keeping .borderless completely frameless inside stat badges.
        Styles.Add(new Style(x => x.OfType<NumericUpDown>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(Layoutable.HeightProperty, 24.0),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(NumericUpDown.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            }
        });

        Styles.Add(new Style(x => x.OfType<NumericUpDown>().Not(y => y.Class("borderless")))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, surfaceInput),
                new Setter(TemplatedControl.BorderBrushProperty, borderControl),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            }
        });

        Styles.Add(new Style(x => x.OfType<NumericUpDown>().Not(y => y.Class("borderless")).Template().OfType<ButtonSpinner>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, surfaceInput),
                new Setter(TemplatedControl.BorderBrushProperty, borderControl),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
            }
        });

        Styles.Add(new Style(x => x.OfType<NumericUpDown>().Class("borderless"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(2, 0)),
            }
        });

        Styles.Add(new Style(x => x.OfType<NumericUpDown>().Class("borderless").Template().OfType<ButtonSpinner>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
            }
        });

        Styles.Add(new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.Medium),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 6)),
                new Setter(Layoutable.MinHeightProperty, 30.0),
                new Setter(TemplatedControl.ForegroundProperty, textSecondary),
            }
        });

        Styles.Add(new Style(x => x.OfType<TabItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.ForegroundProperty, textPrimary),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
            }
        });

        Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 11.0),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TemplatedControl.ForegroundProperty, textSecondary),
                new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#171B28"))),
                new Setter(TemplatedControl.BorderBrushProperty, borderSubtle),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 1, 1)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 5)),
                new Setter(Layoutable.MinHeightProperty, 28.0),
            }
        });

        Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Layoutable.MarginProperty, new Thickness(2, 0)),
            }
        });

        Styles.Add(new Style(x => x.OfType<DataGridCell>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0)),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            }
        });

        Styles.Add(new Style(x => x.OfType<DataGridCell>().Class("numeric"))
        {
            Setters =
            {
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Right),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 0, 8, 0)),
            }
        });

        Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>().Not(y => y.Class(":sortascending")).Not(y => y.Class(":sortdescending")).Template().OfType<Avalonia.Controls.Shapes.Path>())
        {
            Setters =
            {
                new Setter(Visual.IsVisibleProperty, false),
                new Setter(Layoutable.WidthProperty, 0.0),
                new Setter(Layoutable.MinWidthProperty, 0.0),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
            }
        });

        Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>().Not(y => y.Class(":sortascending")).Not(y => y.Class(":sortdescending")).Template().OfType<PathIcon>())
        {
            Setters =
            {
                new Setter(Visual.IsVisibleProperty, false),
                new Setter(Layoutable.WidthProperty, 0.0),
                new Setter(Layoutable.MinWidthProperty, 0.0),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
            }
        });

        Styles.Add(new Style(x => x.OfType<ListBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, surface),
                new Setter(TemplatedControl.BorderBrushProperty, borderSubtle),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
            }
        });

        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 4)),
                new Setter(Layoutable.MinHeightProperty, 26.0),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
            }
        });

        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse("#231B42"))),
            }
        });

        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse("#2B2150"))),
            }
        });

        Styles.Add(new Style(x => x.OfType<AutoCompleteBox>())
        {
            Setters =
            {
                new Setter(Layoutable.HeightProperty, 24.0),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
            }
        });

        Styles.Add(new Style(x => x.OfType<CalendarDatePicker>())
        {
            Setters =
            {
                new Setter(Layoutable.HeightProperty, 24.0),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(TemplatedControl.BackgroundProperty, surfaceInput),
                new Setter(TemplatedControl.BorderBrushProperty, borderControl),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
            }
        });
    }
}
