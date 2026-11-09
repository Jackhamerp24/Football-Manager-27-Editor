using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Fm27Editor.App;

/// <summary>
/// The one look of every table that has a tick column and a Tools card beside it (squads, club staff, national staff):
/// the same grid styling, the same header text, the same columns, the same tick column and the same header sorting.
/// </summary>
internal static class ToolGrid
{
    static IBrush Brush(string hex) => UiKit.Brush(hex);

    public static DataGrid Create()
    {
        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Brush("#1D2130"),
            RowHeight = 29,
            Background = Brush("#11131C"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        grid.Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 0)), new Setter(Layoutable.MinHeightProperty, 26.0) }
        });
        grid.Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters = { new Setter(Layoutable.MarginProperty, new Thickness(0)) }
        });
        grid.Styles.Add(new Style(x => x.OfType<DataGridCell>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)) }
        });
        return grid;
    }

    public static Control Header(string text, string tooltip, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#94A3B8"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = align,
            TextTrimming = TextTrimming.None,
        };
        ToolTip.SetTip(tb, tooltip);
        return tb;
    }

    /// <summary>A right-aligned number column.</summary>
    public static DataGridTemplateColumn Number<T>(string title, Func<T, string> text, double width, string tooltip) => new()
    {
        Header = Header(title, tooltip, HorizontalAlignment.Right),
        Width = new DataGridLength(width),
        CellTemplate = new FuncDataTemplate<T>((row, _) => new TextBlock
        {
            Text = text(row),
            Foreground = Brush("#F1F5F9"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 5, 0),
        }),
    };

    /// <summary>A left-aligned text column; <paramref name="width"/> &lt;= 0 makes it a star column.</summary>
    public static DataGridTemplateColumn Text<T>(string title, Func<T, string> text, double width, string tooltip, string foreground = "#CBD5E1", bool bold = false, double minWidth = 0) => new()
    {
        Header = Header(title, tooltip, HorizontalAlignment.Left),
        Width = width <= 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
        MinWidth = minWidth,
        CellTemplate = new FuncDataTemplate<T>((row, _) =>
        {
            var tb = new TextBlock
            {
                Text = text(row),
                FontWeight = bold ? FontWeight.Medium : FontWeight.Normal,
                Foreground = Brush(foreground),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 4, 0),
            };
            ToolTip.SetTip(tb, text(row));
            return tb;
        }),
    };

    /// <summary>
    /// The first column: a tick per row and a tick in the header for all rows. Every row starts ticked. A click anywhere on a row
    /// leaves only that row ticked; Ctrl+click ticks or unticks that row alone. <paramref name="excluded"/> holds the keys of the
    /// unticked rows; <paramref name="changed"/> runs after any change.
    /// </summary>
    public static void AddTick<T>(DataGrid grid, HashSet<ulong> excluded, Func<T, ulong> key, Func<IEnumerable<T>> rows, Action changed) where T : class
    {
        var syncs = new List<Action>();
        void Changed() { foreach (var sync in syncs.ToArray()) sync(); changed(); }

        var all = new CheckBox { IsChecked = true, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(all, "Include every row");
        all.IsCheckedChanged += (_, _) =>
        {
            if (all.IsChecked == true) excluded.Clear();
            else foreach (var r in rows()) excluded.Add(key(r));
            Changed();
        };
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = all,
            Width = new DataGridLength(34),
            CanUserSort = false,
            CellTemplate = new FuncDataTemplate<T>((row, _) =>
            {
                var box = new CheckBox { MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                bool syncing = false;
                void Sync() { syncing = true; box.IsChecked = !excluded.Contains(key(row)); syncing = false; }
                Sync();
                box.IsCheckedChanged += (_, _) =>
                {
                    if (syncing) return;
                    if (box.IsChecked == true) excluded.Remove(key(row)); else excluded.Add(key(row));
                    Changed();
                };
                syncs.Add(Sync);
                box.DetachedFromVisualTree += (_, _) => syncs.Remove(Sync);
                return box;
            }),
        });

        grid.AddHandler(InputElement.PointerPressedEvent, (object? _, Avalonia.Input.PointerPressedEventArgs e) =>
        {
            if (!e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed || e.Source is not Avalonia.Visual source) return;
            if (source.FindAncestorOfType<CheckBox>(includeSelf: true) is not null) return;
            if (source.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext is not T row) return;
            ulong id = key(row);
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            {
                if (!excluded.Remove(id)) excluded.Add(id);
            }
            else
            {
                excluded.Clear();
                foreach (var r in rows()) if (key(r) != id) excluded.Add(key(r));
            }
            Changed();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Header sorting uses the grid's own: the arrow at the right of the header cell, as in the module lists. Each named column
    /// sorts by its key; rows with no key come first when ascending.
    /// </summary>
    public static void EnableSort<T>(DataGrid grid, IReadOnlyDictionary<string, Func<T, IComparable?>> keys) where T : class
    {
        grid.CanUserSortColumns = true;
        foreach (var column in grid.Columns)
        {
            if (column.Header is TextBlock { Text: { } name } && keys.TryGetValue(name, out var key))
            {
                column.CustomSortComparer = new KeyComparer<T>(key);
                column.CanUserSort = true;
            }
        }
    }

    /// <summary>Replaces the rows and keeps the sort the user chose.</summary>
    public static void SetItems(DataGrid grid, System.Collections.IEnumerable items)
    {
        var saved = grid.CollectionView?.SortDescriptions.ToList();
        grid.ItemsSource = null;
        grid.ItemsSource = items;
        if (saved is { Count: > 0 } && grid.CollectionView is { } view)
            foreach (var description in saved) view.SortDescriptions.Add(description);
    }

    sealed class KeyComparer<T>(Func<T, IComparable?> key) : System.Collections.IComparer where T : class
    {
        public int Compare(object? x, object? y)
        {
            var a = x is T tx ? key(tx) : null;
            var b = y is T ty ? key(ty) : null;
            if (a is null) return b is null ? 0 : -1;
            return b is null ? 1 : a.CompareTo(b);
        }
    }
}