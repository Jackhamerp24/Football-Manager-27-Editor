using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Game;

/// <summary>An attached, build-checked Football Manager 27 process.</summary>
public sealed class GameSession : IDisposable
{
    readonly WindowsProcessMemory _memory;

    public IProcessMemory Memory => _memory;
    public WindowsProcessMemory Process => _memory;
    public GameProcess Game { get; }
    public OffsetProfile Profile { get; }
    public ulong ModuleBase => Game.ModuleBase;
    public bool CanWrite => _memory.CanWrite;

    public PlayerRepository Players { get; }
    public PlayerWriter Writer { get; }

    /// <summary>Current in-game date, read fresh on every call; null if unknown.</summary>
    public DateOnly? ReadGameDate() => GameClock.Read(_memory, Profile, Game.ModuleBase);

    GameSession(WindowsProcessMemory memory, GameProcess game, OffsetProfile profile)
    {
        _memory = memory;
        Game = game;
        Profile = profile;
        Players = new PlayerRepository(memory, profile, game.ModuleBase);
        Writer = new PlayerWriter(memory, profile, game.ModuleBase);
    }

    /// <summary>
    /// Attaches to the running game and picks the profile whose build string is present at its
    /// own RVA. Throws <see cref="UnsupportedBuildException"/> when no profile matches.
    /// </summary>
    public static GameSession Attach(IEnumerable<OffsetProfile> profiles, bool writable)
    {
        var all = profiles.ToList();
        var game = GameProcess.Locate(all.FirstOrDefault()?.Module ?? GameProcess.DefaultModule);
        var memory = new WindowsProcessMemory(game.ProcessId, writable);
        foreach (var profile in all)
            if (ReadBuild(memory, game.ModuleBase + profile.BuildStringRva, profile.Build.Length) == profile.Build)
                return new GameSession(memory, game, profile);
        memory.Dispose();
        throw new UnsupportedBuildException($"No offset profile matches this game build (file version {game.FileVersion}). Run: Fm27Editor.Tools find-offsets");
    }

    static string? ReadBuild(IProcessMemory memory, ulong address, int length)
    {
        var bytes = new byte[length];
        return memory.TryRead(address, bytes) ? Encoding.ASCII.GetString(bytes) : null;
    }

    public static IReadOnlyList<OffsetProfile> LoadProfiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.json").Select(OffsetProfile.LoadFile).ToList();

    public void Dispose() => _memory.Dispose();
}

public sealed class GameNotRunningException(string message) : Exception(message);
public sealed class UnsupportedBuildException(string message) : Exception(message);
