using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// The players the user starred, kept by player ID in <c>favorites.json</c> next to the app. A star only remembers the player;
/// nothing is written to the game.
/// </summary>
static class FavoriteStore
{
    static readonly object Gate = new();
    static HashSet<int>? cache;

    /// <summary>Raised after a player was starred or unstarred.</summary>
    public static event Action? Changed;

    static string FilePath => Path.Combine(AppContext.BaseDirectory, "favorites.json");

    static HashSet<int> Load()
    {
        if (cache is not null) return cache;
        lock (Gate)
        {
            if (cache is not null) return cache;
            try { cache = File.Exists(FilePath) ? [.. JsonSerializer.Deserialize<int[]>(File.ReadAllText(FilePath)) ?? []] : []; }
            catch (Exception) { cache = []; }
            return cache;
        }
    }

    public static int Count => Load().Count;

    public static bool Contains(int? playerId) => playerId is { } id && Load().Contains(id);

    public static void Set(int playerId, bool on)
    {
        var set = Load();
        if (!(on ? set.Add(playerId) : set.Remove(playerId))) return;
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(set.Order().ToArray())); }
        catch (Exception) { }
        Changed?.Invoke();
    }
}

/// <summary>A star toggle: filled when the player is a favourite. It uses the accent colour when on and a grey outline-less shape when off.</summary>
sealed class FavoriteStar : ToggleButton
{
    const string StarPath = "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.63L12,2L9.19,8.63L2,9.24L7.46,13.97L5.82,21L12,17.27Z";
    readonly PathIcon icon = new() { Width = 14, Height = 14, Data = Geometry.Parse(StarPath) };

    protected override Type StyleKeyOverride => typeof(ToggleButton);

    public FavoriteStar(int? playerId)
    {
        Width = 30; MinWidth = 30; Height = 26; MinHeight = 26; Padding = new Thickness(0);
        Content = icon;
        IsEnabled = playerId is not null;
        IsChecked = FavoriteStore.Contains(playerId);
        ToolTip.SetTip(this, "Favourite");
        Paint();
        IsCheckedChanged += (_, _) =>
        {
            Paint();
            if (playerId is { } id) FavoriteStore.Set(id, IsChecked == true);
        };
    }

    void Paint() => icon.Foreground = UiKit.Brush(IsChecked == true ? "#A78BFA" : "#64748B");
}
