using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed record CityRow(CityRecord City, string Name, string Nation, int Id);

/// <summary>Cities: inhabitants range, attraction and where on the map.</summary>
internal sealed class CityScreen(IScreenHost host)
    : EntityScreen<CityRecord, CityRow, Dictionary<CityField, string>>(host, "city", "Cities", "cities", "Search city, nation or ID...")
{
    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "City", Binding = new Binding(nameof(CityRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(CityRow.Nation)), Width = new DataGridLength(184) });
        var id = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(CityRow.Id)), Width = new DataGridLength(115) };
        id.CellStyleClasses.Add("numeric");
        grid.Columns.Add(id);
    }

    protected override IReadOnlyList<CityRow> LoadRows(GameSession session)
    {
        var nations = session.Nations.LoadAll().ToDictionary(n => n.Address, n => n.DisplayName);
        return session.Cities.LoadAll()
            .Select(c => new CityRow(c, c.Name, nations.GetValueOrDefault(c.Nation, ""), c.Id))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    protected override bool Matches(CityRow row, string q) =>
        Fold(row.Name).Contains(q, StringComparison.Ordinal) || Fold(row.Nation).Contains(q, StringComparison.Ordinal)
        || row.Id.ToString(Inv).Contains(q, StringComparison.Ordinal);

    protected override CityRecord RecordOf(CityRow row) => row.City;
    protected override ulong AddressOf(CityRecord record) => record.Address;
    protected override string NameOf(CityRecord record) => record.Name;
    protected override Func<ulong, int, Fm27Editor.Core.History.ChangeLabel?>? LabelsOf(CityRecord record) =>
        Fm27Editor.Core.History.EntityChangeLabels.ForCity(Host.Backend.Session.Profile, record.Address);
    protected override Dictionary<CityField, string> NewDraft() => [];
    protected override bool HasChanges(CityRecord record, Dictionary<CityField, string> draft) => draft.Count > 0;
    protected override CityRecord? Reload(CityRecord record) => Host.Backend.Session.Cities.Read(record.Address);
    protected override CityRow Refresh(CityRow row, CityRecord fresh) =>
        row with { City = fresh, Nation = Host.NationCatalogue.FirstOrDefault(n => n.Address == fresh.Nation)?.DisplayName ?? "" };

    protected override void BuildEditor(CityRecord c, Dictionary<CityField, string> draft, Action changed)
    {
        TextBox Box(CityField f) => FieldBox(draft, f, CityLabels.Format(c, f), 110, changed);
        Control Range()
        {
            var combo = new ComboBox { ItemsSource = CityLabels.InhabitantRanges, MinWidth = 176 };
            combo.SelectedIndex = draft.TryGetValue(CityField.Inhabitants, out var d) && int.TryParse(d, out int cur) ? cur : c.Inhabitants;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                if (combo.SelectedIndex == c.Inhabitants) draft.Remove(CityField.Inhabitants); else draft[CityField.Inhabitants] = combo.SelectedIndex.ToString(Inv);
                changed();
            };
            return combo;
        }

        // From the city as it is now, not the row loaded earlier: Nation can be changed on this page.
        string nation = Host.NationCatalogue.FirstOrDefault(n => n.Address == c.Nation)?.DisplayName ?? "";
        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {c.Id.ToString(Inv)}"));
        if (!string.IsNullOrEmpty(nation) && c.Nation != 0)
        {
            badges.Children.Add(UiKit.MetaDot());
            var natRecord = Host.NationCatalogue.FirstOrDefault(n => n.Address == c.Nation);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                Children = { Host.Graphics.NationFlag(natRecord?.Id, 14), new TextBlock { Text = nation, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center } },
            };
            var link = UiKit.MetaLinkButton(content, highlight: true);
            link.Click += async (_, _) => await Host.OpenNationAsync(c.Nation);
            badges.Children.Add(link);
        }
        else if (!string.IsNullOrEmpty(nation))
        {
            badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(nation));
        }
        HeaderHost.Content = UiKit.EntityBanner(c.Name, badges, null);

        bool links = Host.Backend.Session.CityLinks.Available;
        var city = CardRows("City", [
            ("Unique ID", Label(c.Id.ToString(Inv), "#F1F5F9")),
            .. links ? new (string, Control)[]
            {
                ("Nation", LinkRow(c, draft, changed, CityLink.Nation)),
                ("Local Region", LinkRow(c, draft, changed, CityLink.Region)),
                ("Language", LinkRow(c, draft, changed, CityLink.Language)),
                ("Weather", LinkRow(c, draft, changed, CityLink.Weather)),
            } : [],
            ("Inhabitants Range", Range()),
            ("Attraction", Box(CityField.Attraction)),
        ]);
        var location = CardRows("Location", [
            ("Latitude", Box(CityField.Latitude)),
            ("Longitude", Box(CityField.Longitude)),
            ("Altitude", Box(CityField.Altitude)),
        ]);
        Editor.Children.Add(TwoColumns(city, location));
    }

    // Pending Nation / Weather / Language / Region choices live in the same draft under keys past the real fields; the value is the target's address.
    static CityField Key(CityLink link) => (CityField)(100 + (int)link);
    static bool IsLinkKey(CityField f) => (int)f >= 100;

    readonly Dictionary<CityLink, IReadOnlyList<CityLinkTarget>> linkCatalogues = [];

    string LinkName(CityLink link, ulong target)
    {
        if (target == 0) return "";
        if (link == CityLink.Nation) return Host.NationCatalogue.FirstOrDefault(n => n.Address == target)?.DisplayName ?? "";
        return Host.Backend.Session.CityLinks.NameOf(target) ?? "";
    }

    /// <summary>A value with a Change button, as FMRTE's City page shows Nation, Local Region, Language and Weather.</summary>
    Control LinkRow(CityRecord c, Dictionary<CityField, string> draft, Action changed, CityLink link)
    {
        var links = Host.Backend.Session.CityLinks;
        ulong current = links.Pointer(c, link);
        ulong shown = draft.TryGetValue(Key(link), out var pendingText) && ulong.TryParse(pendingText, out ulong p) ? p : current;
        var text = new TextBlock
        {
            Text = LinkName(link, shown), FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiKit.Brush(shown != current ? "#C4B5FD" : "#F1F5F9"),
        };
        var change = new Button { Content = "Change", Margin = new Thickness(8, 0, 0, 0) };
        change.Click += async (_, _) =>
        {
            ulong? chosen = await ChooseLinkAsync(link);
            if (chosen is not { } target) return;
            if (target == current) draft.Remove(Key(link)); else draft[Key(link)] = target.ToString(Inv);
            text.Text = LinkName(link, target);
            text.Foreground = UiKit.Brush(target != current ? "#C4B5FD" : "#F1F5F9");
            changed();
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { text, change } };
    }

    async Task<ulong?> ChooseLinkAsync(CityLink link)
    {
        var session = Host.Backend.Session;
        if (link == CityLink.Nation)
        {
            var picked = await PickerDialog.Show(Host.Window, "Change Nation", q =>
            {
                string folded = Fold(q);
                return Host.NationCatalogue.GroupBy(n => n.Address).Select(g => g.First())
                    .Where(n => Fold(n.DisplayName).Contains(folded, StringComparison.Ordinal))
                    .OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).Take(200).Select(n => (n.DisplayName, (object)n.Address)).ToList();
            });
            return picked is ulong a ? a : null;
        }
        if (!linkCatalogues.TryGetValue(link, out var catalogue))
        {
            var cities = Rows.Select(r => r.City).ToList();
            linkCatalogues[link] = catalogue = await Task.Run(() => session.CityLinks.Catalogue(cities, link));
        }
        string title = link switch { CityLink.Region => "Change Local Region", CityLink.Language => "Change Language", _ => "Change Weather" };
        var target = await PickerDialog.Show(Host.Window, title, q =>
        {
            string folded = Fold(q);
            return catalogue.Where(t => Fold(t.Name).Contains(folded, StringComparison.Ordinal)).Take(200).Select(t => (t.Name, t)).ToList();
        });
        return target?.Address;
    }

    protected override string? Validate(CityRecord c, Dictionary<CityField, string> draft, out Func<int> write)
    {
        var values = new Dictionary<CityField, double>();
        var targets = new List<(CityLink Link, ulong Expected, ulong Target)>();
        var catalogues = linkCatalogues.ToDictionary(kv => kv.Key, kv => kv.Value);
        write = () => 0;
        int count = draft.Count;
        var links = Host.Backend.Session.CityLinks;
        foreach (var (f, text) in draft)
        {
            if (IsLinkKey(f))
            {
                var link = (CityLink)((int)f - 100);
                if (!ulong.TryParse(text, out ulong target)) return $"{link}: choose a value again";
                targets.Add((link, links.Pointer(c, link), target));
                continue;
            }
            if (!CityLabels.TryParse(f, text, out double v, out var error)) return error;
            values[f] = v;
        }
        var writer = Host.Backend.Session.CityWriter;
        write = () =>
        {
            foreach (var (f, v) in values) writer.Set(c, f, v);
            foreach (var (link, expected, target) in targets) links.Set(c, link, expected, target, catalogues.GetValueOrDefault(link));
            return count;
        };
        return null;
    }
}
