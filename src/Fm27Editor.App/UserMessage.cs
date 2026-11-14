using Fm27Editor.Core.Game;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public static class UserMessage
{
    public static string For(Exception error) => error switch
    {
        GameNotRunningException ex => ex.Message,
        SaveChangedException ex => ex.Message,
        UnsupportedBuildException => AppLanguage.T("This game version is not supported"),
        PartialClauseWriteException => AppLanguage.T("Some clause fields may have been saved. Reload before retrying."),
        StaleObjectException => AppLanguage.T("Player data changed in game; reloaded"),
        MemoryAccessException => AppLanguage.T("Couldn't access game data. Check that a save is loaded and retry."),
        ArgumentOutOfRangeException => AppLanguage.T("Value is outside the allowed range"),
        System.ComponentModel.Win32Exception ex => $"Windows error {ex.NativeErrorCode}: {ex.Message}",
        _ => $"{error.GetType().Name}: {error.Message}"
    };
}
