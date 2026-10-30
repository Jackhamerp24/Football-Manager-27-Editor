using System.Diagnostics;

namespace Fm27Editor.Core.Game;

/// <summary>The running fm.exe and where game_plugin.dll is loaded, without any build check.</summary>
public sealed record GameProcess(int ProcessId, ulong ModuleBase, int ModuleSize, string? FileVersion)
{
    public const string ProcessName = "fm";
    public const string DefaultModule = "game_plugin.dll";

    public ulong ModuleEnd => ModuleBase + (ulong)ModuleSize;

    public static GameProcess Locate(string moduleName = DefaultModule)
    {
        var process = Process.GetProcessesByName(ProcessName).FirstOrDefault()
                      ?? throw new GameNotRunningException("Football Manager 27 (fm.exe) is not running");
        var module = process.Modules.Cast<ProcessModule>()
                         .FirstOrDefault(m => m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                     ?? throw new GameNotRunningException($"{moduleName} is not loaded yet");
        return new GameProcess(process.Id, (ulong)module.BaseAddress, module.ModuleMemorySize, module.FileVersionInfo.FileVersion);
    }
}
