using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
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
        var accentSubtle = new SolidColorBrush(Color.Parse("#2E1D59"));
        var surface = new SolidColorBrush(Color.Parse("#131620"));
        var surfaceElevated = new SolidColorBrush(Color.Parse("#1B1F2E"));
        var surfaceHover = new SolidColorBrush(Color.Parse("#23283B"));
        var border = new SolidColorBrush(Color.Parse("#282E42"));
        var textPrimary = new SolidColorBrush(Color.Parse("#F1F5F9"));
        var textSecondary = new SolidColorBrush(Color.Parse("#94A3B8"));

        Resources["SystemAccentColor"] = Color.Parse("#7C3AED");
        Resources["SystemAccentColorDark1"] = Color.Parse("#6D28D9");
        Resources["SystemAccentColorLight1"] = Color.Parse("#8B5CF6");

        // Keep scrollbars always expanded so thumbs are easy to grab and drag.
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
                new Setter(TemplatedControl.BackgroundProperty, border),
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
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.FontSizeProperty, 12.5),
                new Setter(Layoutable.HeightProperty, 26.0),
                new Setter(Layoutable.MinHeightProperty, 26.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(TemplatedControl.BackgroundProperty, surfaceElevated),
                new Setter(TemplatedControl.BorderBrushProperty, border),
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
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.FontSizeProperty, 12.5),
                new Setter(Layoutable.HeightProperty, 26.0),
                new Setter(Layoutable.MinHeightProperty, 26.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(7, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.BackgroundProperty, surface),
                new Setter(TemplatedControl.BorderBrushProperty, border),
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

        Styles.Add(new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.FontSizeProperty, 12.5),
                new Setter(Layoutable.HeightProperty, 26.0),
                new Setter(Layoutable.MinHeightProperty, 26.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.BackgroundProperty, surface),
                new Setter(TemplatedControl.BorderBrushProperty, border),
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

        Styles.Add(new Style(x => x.OfType<NumericUpDown>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.FontSizeProperty, 12.5),
                new Setter(Layoutable.HeightProperty, 26.0),
                new Setter(Layoutable.MinHeightProperty, 26.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(NumericUpDown.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.BackgroundProperty, surface),
                new Setter(TemplatedControl.BorderBrushProperty, border),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            }
        });

        Styles.Add(new Style(x => x.OfType<NumericUpDown>().Descendant().OfType<TextBox>())
        {
            Setters =
            {
                new Setter(Layoutable.HeightProperty, double.NaN),
                new Setter(Layoutable.MinHeightProperty, 0.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Stretch),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            }
        });

        Styles.Add(new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.Medium),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 6)),
                new Setter(Layoutable.MinHeightProperty, 32.0),
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
                new Setter(TemplatedControl.FontSizeProperty, 11.5),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TemplatedControl.ForegroundProperty, textSecondary),
                new Setter(TemplatedControl.BackgroundProperty, surfaceElevated),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 5)),
                new Setter(Layoutable.MinHeightProperty, 29.0),
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
                new Setter(TemplatedControl.FontSizeProperty, 12.5),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0)),
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

        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 4)),
                new Setter(Layoutable.MinHeightProperty, 26.0),
                new Setter(TemplatedControl.FontSizeProperty, 12.5),
            }
        });
    }
}
