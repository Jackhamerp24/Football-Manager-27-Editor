using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    sealed record KitOption(ClubKitItem Item, string Label)
    {
        public override string ToString() => Label;
    }
    readonly Dictionary<ulong, int> selectedKitTab = [];
    readonly Dictionary<(ulong Club, int Tab), int> selectedKitPart = [];
    readonly Dictionary<ulong, Dictionary<ulong, KitDraft>> kitDrafts = [];
    readonly Dictionary<ulong, string> clubFamiliarityDrafts = [];
    readonly Dictionary<ulong, ClubAffiliationEntry> pendingAffiliationRemovals = [];

    static int StyleLimit(ClubKitItem item) => (item.Type, item.Flags) switch
    {
        (3, 0x100) => 56,   // shirt
        (2, 0x100) => 56,   // text
        (2, 0) => 56,       // GK shirt
        (1, 0) or (0x21, 0) => 56,   // icon
        (4, 0) or (4, 1) => 10,      // shorts
        (5, 0) or (5, 1) => 15,      // socks
        _ => 56,
    };

    void UpdateKitSaveButtons(ulong club)
    {
        bool pending = (kitDrafts.TryGetValue(club, out var drafts) && drafts.Count > 0) || pendingAffiliationRemovals.ContainsKey(club) || clubFamiliarityDrafts.ContainsKey(club);
        bool scalar = clubDrafts.TryGetValue(club, out var draft) && draft.CanSave;
        bool invalidScalar = draft is not null && draft.InvalidCount > 0;
        if (clubScreen.SaveButton is { } save) save.IsEnabled = !invalidScalar && (pending || scalar);
    UpdateClubStatusFromDraft();
    }

    void SetKitDraft(ClubKitItem item, int? colourIndex, string text, Action? onDraftChanged = null)
    {
        if (!kitDrafts.TryGetValue(item.Club, out var drafts)) kitDrafts[item.Club] = drafts = [];
        if (!drafts.TryGetValue(item.Slot, out var draft)) drafts[item.Slot] = draft = new(item);
        if (colourIndex is { } index)
        {
            if (string.Equals(text.Trim(), $"#{draft.Snapshot.Colour(index):X8}", StringComparison.OrdinalIgnoreCase)) draft.Colours.Remove(index);
            else draft.Colours[index] = text;
        }
        else
        {
            // In-game editor style counts: shirt/text/icon 56, shorts 10, socks 15.
            string style = text.Trim();
            if (int.TryParse(style, out int parsed))
            {
                int limit = StyleLimit(item);
                int clamped = Math.Clamp(parsed, 1, limit);
                if (clamped != parsed) style = clamped.ToString(Inv);
            }
            draft.Style = style == draft.Snapshot.Style.ToString(Inv) ? null : style;
        }
        if (draft.Colours.Count == 0 && draft.Style is null) drafts.Remove(item.Slot);
        if (drafts.Count == 0) kitDrafts.Remove(item.Club);
        UpdateKitSaveButtons(item.Club);
        onDraftChanged?.Invoke();
    }

    void SaveKitDrafts(GameSession session, ClubEntry club)
    {
        if (!kitDrafts.TryGetValue(club.Address, out var drafts) || drafts.Count == 0) return;
        var parsed = new List<(KitDraft Draft, List<(int Index, uint Value)> Colours, int? Style)>();
        foreach (var draft in drafts.Values)
        {
            var colours = new List<(int, uint)>();
            foreach (var (index, text) in draft.Colours)
            {
                string value = text.Trim().TrimStart('#');
                if (value.Length != 8 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, Inv, out uint colour))
                    throw new FormatException("Colour must be #AARRGGBB");
                colours.Add((index, colour));
            }
            int? style = null;
            if (draft.Style is { } raw)
            {
                if (!int.TryParse(raw.Trim(), out int value) || value < 1 || value > StyleLimit(draft.Snapshot))
                    throw new FormatException($"Item Style must be 1 to {StyleLimit(draft.Snapshot)}");
                style = value;
            }
            parsed.Add((draft, colours, style));
        }
        var current = session.ClubKits.Read(club.Address, club.Id!.Value).ToDictionary(x => x.Slot);
        foreach (var (draft, _, _) in parsed)
            if (!current.TryGetValue(draft.Snapshot.Slot, out var actual) || actual != draft.Snapshot)
                throw new Fm27Editor.Core.Players.StaleObjectException("Kit item changed since editing began");
        foreach (var (draft, colours, style) in parsed)
        {
            var snapshot = draft.Snapshot;
            foreach (var (index, value) in colours)
            {
                backend.Edits.Execute("Club", club.DisplayName, "Page save", () => session.ClubKits.SetColour(snapshot, index, value));
                snapshot = session.ClubKits.Read(club.Address, club.Id!.Value).Single(x => x.Slot == snapshot.Slot);
                draft.Colours.Remove(index);
            }
            if (style is { } valueStyle)
            {
                backend.Edits.Execute("Club", club.DisplayName, "Page save", () => session.ClubKits.SetStyle(snapshot, valueStyle));
                draft.Style = null;
            }
            drafts.Remove(draft.Snapshot.Slot);
        }
        kitDrafts.Remove(club.Address);
    }

    void RenderClubKits(GameSession session, ClubEntry club)
    {
        if (club.Id is not { } id) return;
        var items = session.ClubKits.Read(club.Address, id);
        if (items.Count == 0) return;
        var tabs = BuildKitsControl(club.Address, items, () => UpdateKitSaveButtons(club.Address));
        clubEditorPanel.Children.Add(tabs);
    }

    Control BuildKitsControl(ulong ownerAddress, IReadOnlyList<ClubKitItem> items, Action onDraftChanged)
    {
        var options = new List<KitOption>();
        foreach (var item in items.Where(x => x.Kit <= 2))
        {
            string? part = (item.Type, item.Flags) switch
            {
                (3, 0x100) => "Shirt", (2, 0) => "GK Shirt", (2, 0x100) => "Text",
                (1 or 0x21, 0) => "Icon", (4, 0 or 1) => "Shorts", (5, 0 or 1) => "Socks",
                _ => null,
            };
            if (part is null) continue;
            string kit = item.Kit switch { 0 => "Home", 1 => "Away", _ => "Third" };
            options.Add(new(item, part.StartsWith("GK ", StringComparison.Ordinal) ? $"GK {kit} {part[3..]}" : $"{kit} {part}"));
        }
        // FMRTE traces identify GK shorts/socks relative to the corresponding outfield item.
        for (byte kitIndex = 0; kitIndex < 3; kitIndex++)
        {
            string kit = kitIndex switch { 0 => "Home", 1 => "Away", _ => "Third" };
            if (ClubKitMapping.GoalkeeperIcon(items, kitIndex) is { } icon)
                options.Add(new(icon, $"GK {kit} Icon"));
            foreach (var (type, part) in new[] { (4, "Shorts"), (5, "Socks") })
                if (ClubKitMapping.GoalkeeperPart(items, kitIndex, (byte)type) is { } item)
                    options.Add(new(item, $"GK {kit} {part}"));
        }
        string[] tabNames = ["Home Kit", "Away Kit", "Third Kit", "GK Home Kit", "GK Away Kit", "GK Third Kit"];
        string[] kitNames = ["Home", "Away", "Third", "GK Home", "GK Away", "GK Third"];
        string[] partNames = ["Shirt", "Text", "Icon", "Shorts", "Socks"];
        var pages = new List<(string Title, Control Content)>();

        uint EffectiveColour(ClubKitItem item, int index)
        {
            if (kitDrafts.GetValueOrDefault(ownerAddress)?.GetValueOrDefault(item.Slot)?.Colours.TryGetValue(index, out var raw) == true)
            {
                string hex = raw.Trim().TrimStart('#');
                if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, Inv, out uint parsed))
                    return parsed;
                if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, Inv, out uint rgb))
                    return rgb | 0xFF000000u;
            }
            return item.Colour(index);
        }

        void Show(StackPanel host, KitOption option, Action onEdited)
        {
            host.Children.Clear();
            var item = option.Item;
            var panel = new StackPanel { Spacing = 6, Margin = new Thickness(12, 10) };
            string[] names = ["Foreground", "Background", "Outline", "Number Colour", "Number Outline Colour"];
            for (int i = 0; i < names.Length; i++)
            {
                int colourIndex = i;
                string original = $"#{item.Colour(i):X8}";
                string text = kitDrafts.GetValueOrDefault(ownerAddress)?.GetValueOrDefault(item.Slot)?.Colours.GetValueOrDefault(i) ?? original;
                var box = new TextBox { Text = text, Width = 110 };
                box.TextChanged += (_, _) => { SetKitDraft(item, colourIndex, box.Text ?? "", onDraftChanged); onEdited(); };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = names[i], Width = 148, VerticalAlignment = VerticalAlignment.Center }, ColourInput(box) } });
            }
            var style = new TextBox { Text = kitDrafts.GetValueOrDefault(ownerAddress)?.GetValueOrDefault(item.Slot)?.Style ?? item.Style.ToString(Inv), Width = 110, HorizontalContentAlignment = HorizontalAlignment.Right };
            style.TextChanged += (_, _) => SetKitDraft(item, null, style.Text ?? "", onDraftChanged);
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Item Style", Width = 148, VerticalAlignment = VerticalAlignment.Center }, style } });
            host.Children.Add(panel);
        }

        for (int t = 0; t < tabNames.Length; t++)
        {
            int tabIndex = t;
            string prefix = kitNames[t];
            var preview = UiKit.CreateKitPreview(tabNames[t]);

            void RefreshPreview()
            {
                var shirt = options.FirstOrDefault(x => x.Label == $"{prefix} Shirt")?.Item;
                var textItem = options.FirstOrDefault(x => x.Label == $"{prefix} Text")?.Item;
                var shorts = options.FirstOrDefault(x => x.Label == $"{prefix} Shorts")?.Item;
                var socks = options.FirstOrDefault(x => x.Label == $"{prefix} Socks")?.Item;
                if (shirt is null) return;
                uint shirtFg = EffectiveColour(shirt, 0);
                uint shirtBg = EffectiveColour(shirt, 1);
                uint shirtOutline = EffectiveColour(shirt, 2);
                uint numFill = textItem is not null ? EffectiveColour(textItem, 0) : EffectiveColour(shirt, 3);
                uint numOutline = textItem is not null ? EffectiveColour(textItem, 2) : EffectiveColour(shirt, 4);
                uint? shortsBg = shorts is not null ? EffectiveColour(shorts, 1) : null;
                uint? socksBg = socks is not null ? EffectiveColour(socks, 1) : null;
                preview.Update(shirtBg, shirtFg, shirtOutline, numFill, numOutline, shortsBg, socksBg);
            }

            var host = new StackPanel();
            var list = new ListBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
            };
            var rowOptions = new List<KitOption?>();
            foreach (string part in partNames)
            {
                string name = $"{kitNames[t]} {part}";
                var matches = options.Where(x => x.Label == name).ToArray();
                if (matches.Length == 0)
                {
                    rowOptions.Add(null);
                    list.Items.Add(new ListBoxItem { Content = part, IsEnabled = false });
                    continue;
                }
                foreach (var option in matches)
                {
                    rowOptions.Add(option);
                    list.Items.Add(new ListBoxItem { Content = part });
                }
            }
            list.SelectionChanged += (_, _) =>
            {
                int part = list.SelectedIndex;
                if (part < 0 || part >= rowOptions.Count || rowOptions[part] is not { } option) return;
                selectedKitPart[(ownerAddress, tabIndex)] = part;
                Show(host, option, RefreshPreview);
            };
            int previous = selectedKitPart.GetValueOrDefault((ownerAddress, t), 0);
            int chosen = previous >= 0 && previous < rowOptions.Count && rowOptions[previous] is not null
                ? previous : rowOptions.FindIndex(x => x is not null);
            if (chosen >= 0) list.SelectedIndex = chosen;
            RefreshPreview();

            var partCard = UiKit.Card("Kit Part", list);
            partCard.VerticalAlignment = VerticalAlignment.Top;
            var editCard = UiKit.Card("Colours & Style", host);
            editCard.VerticalAlignment = VerticalAlignment.Top;

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("156,*,186"),
                ColumnSpacing = 10,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Grid.SetColumn(partCard, 0);
            Grid.SetColumn(editCard, 1);
            Grid.SetColumn(preview.Control, 2);
            grid.Children.Add(partCard);
            grid.Children.Add(editCard);
            grid.Children.Add(preview.Control);
            pages.Add((tabNames[t], grid));
        }
        int start = Math.Clamp(selectedKitTab.GetValueOrDefault(ownerAddress), 0, pages.Count - 1);
        return UiKit.OptionTabs(pages, start, index => selectedKitTab[ownerAddress] = index);
    }
}

/// <summary>Pending edits of one kit item: colour texts by index and the style text.</summary>
internal sealed class KitDraft(ClubKitItem snapshot)
{
    public ClubKitItem Snapshot { get; } = snapshot;
    public Dictionary<int, string> Colours { get; } = [];
    public string? Style { get; set; }
}
