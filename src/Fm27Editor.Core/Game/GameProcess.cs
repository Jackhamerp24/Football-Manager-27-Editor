using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace Fm27Editor.Core.Game;

/// <summary>The running fm.exe and where game_plugin.dll is loaded, without any build check.</summary>
public sealed record GameProcess(int ProcessId, ulong ModuleBase, int ModuleSize, string? FileVersion, string? ExePath = null)
{
    public const string ProcessName = "fm";
    public const string DefaultModule = "game_plugin.dll";

    public ulong ModuleEnd => ModuleBase + (ulong)ModuleSize;

    public static GameProcess Locate(string moduleName = DefaultModule)
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0) throw new GameNotRunningException("Football Manager 27 (fm.exe) is not running");
        // Another FM version can run at the same time (both are fm.exe); prefer the one whose module is 27.x.
        var candidates = processes
            .Select(p => (p, m: TryModule(p, moduleName)))
            .Where(x => x.m is not null)
            .OrderByDescending(x => x.m!.FileVersionInfo.FileVersion?.StartsWith("27.") == true)
            .ToList();
        if (candidates.Count == 0) throw new GameNotRunningException($"{moduleName} is not loaded yet");
        var (process, module) = candidates[0];
        return new GameProcess(process.Id, (ulong)module!.BaseAddress, module.ModuleMemorySize, module.FileVersionInfo.FileVersion, module.FileName);
    }

    static ProcessModule? TryModule(Process p, string moduleName)
    {
        try { return p.Modules.Cast<ProcessModule>().FirstOrDefault(m => m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase)); }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static IReadOnlyList<GameProcess> LocateAll(string moduleName = DefaultModule)
    {
        var processes = FindProcesses();
        if (processes.Length == 0) throw new GameNotRunningException("Football Manager (fm.exe) is not running");
        var games = new List<GameProcess>();
        Exception? error = null;
        foreach (var processId in processes)
        {
            var snapshot = CreateToolhelp32Snapshot(0x18, (uint)processId);
            if (snapshot == new IntPtr(-1)) { error = new Win32Exception(Marshal.GetLastWin32Error()); continue; }
            try
            {
                var module = new ModuleEntry { Size = (uint)Marshal.SizeOf<ModuleEntry>(), ModuleName = "", ExePath = "" };
                if (!Module32First(snapshot, ref module)) { error = new Win32Exception(Marshal.GetLastWin32Error()); continue; }
                do
                {
                    if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    { games.Add(new GameProcess(processId, (ulong)module.BaseAddress, checked((int)module.BaseSize), null, module.ExePath)); break; }
                } while (Module32Next(snapshot, ref module));
            }
            finally { CloseHandle(snapshot); }
        }
        if (games.Count == 0 && error is not null) throw new GameNotRunningException($"Cannot inspect game processes: {error.Message}");
        if (games.Count == 0) throw new GameNotRunningException($"{moduleName} is not loaded yet");
        return games;
    }

    static int[] FindProcesses()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not list processes");
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), ExeFile = "" };
            var found = new List<int>();
            if (!Process32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read process list");
            do
            {
                if (string.Equals(entry.ExeFile, "fm.exe", StringComparison.OrdinalIgnoreCase))
                    found.Add((int)entry.ProcessId);
            } while (Process32Next(snapshot, ref entry));
            return found.ToArray();
        }
        finally { CloseHandle(snapshot); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ModuleEntry
    {
        public uint Size, ModuleId, ProcessId, GlobalUsage, ProcessUsage;
        public IntPtr BaseAddress;
        public uint BaseSize;
        public IntPtr ModuleHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ModuleName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExePath;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Module32First(IntPtr snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Module32Next(IntPtr snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
