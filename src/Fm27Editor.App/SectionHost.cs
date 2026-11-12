using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;

namespace Fm27Editor.App;

/// <summary>
/// FMRTE-style left rail: the sections of one entity listed vertically, the chosen section filling the rest.
/// The rail order can be dragged and is remembered per entity kind.
/// </summary>
static class SectionHost
{
    public sealed record Section(string Key, string Title, Func<Control>? Factory = null, Control? Prebuilt = null)
    {
        public Section(string key, string title, Control body) : this(key, title, null, body) { }
        public Section(string key, string title, Func<Control> factory) : this(key, title, factory, null) { }

        Control? _body = Prebuilt;
        public Control Body => _body ??= (Factory?.Invoke() ?? new Panel());
    }

    sealed class Entry(Section section) : System.ComponentModel.INotifyPropertyChanged
    {
        public Section Section { get; } = section;
        bool dirty;
        public bool Dirty { get => dirty; set { if (dirty == value) return; dirty = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Label))); } }
        public string Label => Dirty ? Section.Title + " *" : Section.Title;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => Label;
    }
    /// <summary>Rail lists by entity, so the shell can put a star on the section that holds unsaved edits.</summary>
    static readonly Dictionary<string, (ListBox List, List<Entry> Items)> Rails = [];
    static readonly Dictionary<string, HashSet<string>> DirtyKeys = [];

    /// <summary>
    /// Sections whose own (hidden) Save button is enabled: every child tab with its own pending edits (Relationships,
    /// Contract, Languages, Injury …) keeps a hidden "Save changes"-style button that the shell's one Save presses. Its
    /// IsEnabled is the tab's "has unsaved edits", so the star follows it without each tab reporting anything.
    /// </summary>
    static readonly Dictionary<string, HashSet<string>> WatchedKeys = [];
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Tuple<string, string>> Bodies = new();

    /// <summary>Raised when a section's own pending edits appear (true) or are all gone (false).</summary>
    public static event Action<string, bool>? SectionEdited;

    static readonly string[] SaveLabels = ["Save changes", "Save contract", "Save loan", "Save"];
    static bool IsPendingSave(Button b) => !b.IsVisible && b.IsEnabled && b.Content is string label && SaveLabels.Contains(label);

    static SectionHost()
    {
        InputElement.IsEnabledProperty.Changed.AddClassHandler<Button>((button, _) => OnSaveButtonChanged(button));
    }

    static void OnSaveButtonChanged(Button button)
    {
        if (button.IsVisible || button.Content is not string label || !SaveLabels.Contains(label)) return;
        foreach (var ancestor in button.GetLogicalAncestors().OfType<Control>())
        {
            if (!Bodies.TryGetValue(ancestor, out var owner)) continue;
            var (entity, key) = (owner.Item1, owner.Item2);
            bool dirty = ancestor.GetLogicalDescendants().OfType<Button>().Any(IsPendingSave);
            var set = WatchedKeys.TryGetValue(entity, out var existing) ? existing : (WatchedKeys[entity] = []);
            bool changed = dirty ? set.Add(key) : set.Remove(key);
            if (!changed) return;
            Apply(entity);
            SectionEdited?.Invoke(entity, dirty);
            return;
        }
    }

    static readonly Dictionary<string, string> SelectedKey = [];
    const string DragFormat = "fm27-section";

    /// <summary>Key of the section currently open in the rail, or null when that entity's rail is not built.</summary>
    public static string? Selected(string entity) => Rails.TryGetValue(entity, out var rail) ? (rail.List.SelectedItem as Entry)?.Section.Key : null;

    /// <summary>Stars every section in that rail whose key is in <paramref name="keys"/> (FMRTE marks each child tab that holds unsaved edits).</summary>
    public static void MarkDirty(string entity, IReadOnlyCollection<string> keys)
    {
        var set = DirtyKeys.TryGetValue(entity, out var existing) ? existing : (DirtyKeys[entity] = []);
        set.Clear();
        set.UnionWith(keys);
        Apply(entity);
    }

    static void Apply(string entity)
    {
        if (!Rails.TryGetValue(entity, out var rail)) return;
        var keys = new HashSet<string>(DirtyKeys.TryGetValue(entity, out var set) ? set : []);
        if (WatchedKeys.TryGetValue(entity, out var watched)) keys.UnionWith(watched);
        bool changed = false;
        foreach (var item in rail.Items)
        {
            bool dirty = keys.Contains(item.Section.Key);
            if (item.Dirty == dirty) continue;
            item.Dirty = dirty;
            changed = true;
        }
        _ = changed;
    }
    public static Control Build(string entity, IReadOnlyList<Section> sections, string? selectKey = null, Action<string>? onSelected = null, ContentControl? railHost = null)
    {
        if (sections.Count == 0) return new Panel();

        if (selectKey is not null) SelectedKey[entity] = selectKey;
        // New bodies: the old ones' pending edits went with them.
        WatchedKeys.Remove(entity);

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
        list.ItemTemplate = new FuncDataTemplate<Entry>((entry, _) =>
        {
            var label = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            label.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Entry.Label)));
            return label;
        }, false);

        void Select(int index)
        {
            if (index < 0 || index >= items.Count) index = 0;
            list.SelectedIndex = index;
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Entry entry) return;
            content.Content = entry.Section.Body;
            Bodies.AddOrUpdate(entry.Section.Body, Tuple.Create(entity, entry.Section.Key));
            SelectedKey[entity] = entry.Section.Key;
            onSelected?.Invoke(entry.Section.Key);
        };

        list.ItemsSource = items;
        // Registered at once (it used to be only after a drag), so MarkDirty and Selected find this rail.
        Rails[entity] = (list, items);
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
            Rails[entity] = (list, items);
            list.SelectedItem = moved;
            UiOrderStore.Save(entity, [.. items.Select(i => i.Section.Key)]);
        });

        Apply(entity);
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
