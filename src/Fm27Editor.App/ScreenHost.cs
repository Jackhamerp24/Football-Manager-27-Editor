using Avalonia.Controls;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App;

/// <summary>The window as the entity screens see it.</summary>
public sealed partial class MainWindow : IScreenHost
{
    readonly List<IEntityScreen> screens = [];
    AwardScreen awardScreen = null!;
    CityScreen cityScreen = null!;
    RefereeScreen refereeScreen = null!;
    StadiumScreen stadiumScreen = null!;
    NationScreen nationScreen = null!;
    CompetitionScreen competitionScreen = null!;

    /// <summary>Creates a screen, remembers it, and lets it add itself to the module row. Call in the order the modules should appear.</summary>
    T AddScreen<T>(Func<IScreenHost, T> create) where T : class, IEntityScreen, IRegisterable
    {
        var screen = create(this);
        screens.Add(screen);
        screen.Register();
        return screen;
    }

    internal static string Fold(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    }

    EditorBackend IScreenHost.Backend => backend;
    GraphicsCatalog IScreenHost.Graphics => graphics;
    void IScreenHost.SetStatus(string text) => status.Text = text;

    void IScreenHost.AddModule(string title, Control root, object navItem)
    {
        AddModuleTab(title, root, navItem);
        modules.ItemsSource = (object[])[.. modules.ItemsSource!.Cast<object>(), navItem];
    }

    object? IScreenHost.SelectedNav => modules.SelectedItem;

    event EventHandler IScreenHost.NavChanged
    {
        add => modules.SelectionChanged += (s, _) => value(s, EventArgs.Empty);
        remove { }
    }

    bool IScreenHost.IsShowing(Control body) => ReferenceEquals(bodyHost.Content, body);
    void IScreenHost.OpenTab(string kind, ulong address, string title, Control body, Action restore) => OpenKindTab(kind, address, title, body, restore);
    void IScreenHost.SwitchModule(string title) => SwitchModule(title);

    IReadOnlyList<NationRecord> IScreenHost.NationCatalogue =>
        nationCatalogue ??= backend.Session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

    Task IScreenHost.OpenNationAsync(ulong address) => OpenPlayerNationAsync(address);
    Task IScreenHost.OpenClubAsync(ulong address) => OpenPlayerClubAsync(address);
    void IScreenHost.OpenCity(ulong address)
    {
        if (backend.Session.Cities.Read(address) is { } city) cityScreen.Pick(city); else status.Text = "City unavailable";
    }
    void IScreenHost.OpenStadium(ulong address)
    {
        if (backend.Session.Stadiums.Reload(address) is { } stadium) stadiumScreen.Pick(stadium); else status.Text = "Stadium unavailable";
    }
    Avalonia.Controls.Window IScreenHost.Window => this;
    void IScreenHost.OpenStaff(Fm27Editor.Core.Staff.StaffRecord staff) => OpenClubStaff(staff);
    IReadOnlyList<Fm27Editor.Core.Players.PlayerRecord> IScreenHost.Players() => Players();
    IReadOnlyList<Fm27Editor.Core.Clubs.ClubEntry> IScreenHost.Clubs() => clubCache ??= backend.Session.LoadClubs(Players());
    Control IScreenHost.BuildKits(ulong owner, IReadOnlyList<Fm27Editor.Core.Clubs.ClubKitItem> items, Action changed) => BuildKitsControl(owner, items, changed);
    IReadOnlyDictionary<ulong, KitDraft>? IScreenHost.KitDraftOf(ulong owner) => kitDrafts.GetValueOrDefault(owner);
    void IScreenHost.ClearKitDraft(ulong owner) => kitDrafts.Remove(owner);
    Control IScreenHost.BuildSquad(Func<IReadOnlyList<Fm27Editor.Core.Players.PlayerRecord>> getSquad) => BuildSquadGridAndActions(getSquad, isNation: true, out _);

    /// <summary>The thing being edited, from the screen showing it.</summary>
    (string Kind, string Entity)? ScreenEditContext() => screens.Select(s => s.EditContext).FirstOrDefault(c => c is not null);
}
