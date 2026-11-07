using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Fm27Editor.App;

/// <summary>
/// FMRTE-style left rail: the sections of one entity listed vertically, the chosen section filling the rest.
/// The rail order can be dragged and is remembered per entity kind.
/// </summary>
static class SectionHost
{
    public sealed record Section(string Key, string Title, Control Body);

    sealed class Entry(Section section)
    {
        public Section Section { get; } = section;
        public override string ToString() => Section.Title;
    }

    static readonly Dictionary<string, string> SelectedKey = [];
    const string DragFormat = "fm27-section";

    public static Control Build(string entity, IReadOnlyList<Section> sections, string? selectKey = null, Action<string>? onSelected = null, ContentControl? railHost = null)
    {
        if (sections.Count == 0) return new Panel();

        if (selectKey is not null) SelectedKey[entity] = selectKey;

        var byKey = sections.ToDictionary(s => s.Key, StringComparer.Ordinal);
        var order = UiOrderStore.Order(entity, [.. sections.Select(s => s.Key)]);
        var items = order.Select(k => new Entry(byKey[k])).ToList();

        var content = new ContentControl
        {
            Margin = entity == "player" ? new Thickness(6, 0, 0, 0) : new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        var list = CreateRailListBox(stretchVertically: entity != "player");
        list.ItemTemplate = new FuncDataTemplate<Entry>((entry, _) => new TextBlock
        {
            Text = entry.Section.Title,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        }, true);

        void Select(int index)
        {
            if (index < 0 || index >= items.Count) index = 0;
            list.SelectedIndex = index;
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Entry entry) return;
            content.Content = entry.Section.Body;
            SelectedKey[entity] = entry.Section.Key;
            onSelected?.Invoke(entry.Section.Key);
        };

        list.ItemsSource = items;
        int start = SelectedKey.TryGetValue(entity, out var key) ? items.FindIndex(i => i.Section.Key == key) : 0;
        Select(start);

        int dragFrom = -1;
        Point? pressed = null;

        int IndexAt(double y)
        {
            for (int i = 0; i < list.ItemCount; i++)
            {
                if (list.ContainerFromIndex(i) is not Control container) continue;
                var bounds = container.Bounds;
                if (y >= bounds.Top && y <= bounds.Bottom) return i;
            }
            return list.ItemCount > 0 && y > 0 ? list.ItemCount - 1 : 0;
        }

        list.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
            pressed = e.GetPosition(list);
            dragFrom = IndexAt(pressed.Value.Y);
        };

        list.PointerMoved += async (_, e) =>
        {
            if (pressed is not { } startPos) return;
            var position = e.GetPosition(list);
            if (Math.Abs(position.Y - startPos.Y) < 8) return;
            if (dragFrom < 0 || dragFrom >= items.Count) return;
            pressed = null;
            var data = new DataObject();
            data.Set(DragFormat, dragFrom);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        };

        list.PointerReleased += (_, _) => pressed = null;

        DragDrop.SetAllowDrop(list, true);
        list.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Move);
        list.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (!e.Data.Contains(DragFormat)) return;
            if (e.Data.Get(DragFormat) is not int from || from < 0 || from >= items.Count) return;
            int to = IndexAt(e.GetPosition(list).Y);
            if (to < 0 || to >= items.Count || to == from) return;

            var moved = items[from];
            items.RemoveAt(from);
            items.Insert(to, moved);
            list.ItemsSource = null;
            list.ItemsSource = items;
            list.SelectedItem = moved;
            UiOrderStore.Save(entity, [.. items.Select(i => i.Section.Key)]);
        });

        if (railHost is not null)
        {
            railHost.Content = list;
            return content;
        }

        var host = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(new GridLength(entity == "player" ? 144 : 168)) { MinWidth = 140 },
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        Grid.SetColumn(list, 0);
        Grid.SetColumn(content, 1);
        host.Children.Add(list);
        host.Children.Add(content);
        return host;
    }

    public static ListBox CreateRailListBox(bool stretchVertically = true)
    {
        var list = new ListBox
        {
            VerticalAlignment = stretchVertically ? VerticalAlignment.Stretch : VerticalAlignment.Top,
            Background = UiKit.Brush("#10131D"),
            BorderThickness = new Thickness(1),
            BorderBrush = UiKit.BorderSubtle,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3),
        };
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(5, 4)),
                new Setter(TemplatedControl.MinHeightProperty, 25.0),
                new Setter(TemplatedControl.MarginProperty, new Thickness(0, 1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
                new Setter(TemplatedControl.ForegroundProperty, UiKit.TextMuted),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(2, 0, 0, 0)),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            }
        });
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.ForegroundProperty, UiKit.TextPrimary),
            }
        });
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TemplatedControl.BorderBrushProperty, UiKit.Accent),
            }
        });
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, UiKit.Brush("#231B42")),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, UiKit.Accent),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderThicknessProperty, new Thickness(2.5, 0, 0, 0)),
            }
        });
        return list;
    }
}
