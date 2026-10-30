using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public static class UserMessage
{
    public static string For(Exception error) => error switch
    {
        GameNotRunningException => "Football Manager 27 is not running",
        UnsupportedBuildException => "This game version is not supported",
        StaleObjectException => "Player data changed in game; reloaded",
        MemoryAccessException => "Couldn't access game data. Check that a save is loaded and retry.",
        ArgumentOutOfRangeException => "Value is outside the allowed range",
        _ => "Something went wrong. Check the game and retry."
    };
}
