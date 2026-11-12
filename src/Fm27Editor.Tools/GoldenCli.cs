using Fm27Editor.Core.Game;
using Fm27Editor.Core.Presets;

/// <summary>
/// <c>golden status</c> — where the regen code is, whether the hook is in, how often it ran, its settings and the last regens.
/// <c>golden on &lt;min PA&gt; &lt;max PA&gt; [wonderkid %] [club|nation|continent ids…] [--write]</c> — switch the hook on (installs on first use).
/// <c>golden off [--write]</c> — keep the hook but stop changing regens. <c>golden install [--write]</c> — hook in, switched off
/// (it still logs every regen). <c>golden uninstall [--write]</c> — put the game's bytes back.
/// Without --write the write commands only print what they would do.
/// </summary>
static class GoldenCli
{
    public static int Run(string[] args, Func<bool, GameSession> attach)
    {
        bool write = args.Contains("--write");
        var rest = args.Where(a => a != "--write").ToArray();
        string command = rest.FirstOrDefault() ?? "status";
        using var session = attach(write && command != "status");
        var hook = session.GoldenGenerationHook;
        ulong site = hook.FindSite();
        Console.WriteLine($"build {session.Profile.Build}; site rva 0x{site - session.Game.ModuleBase:X}");
        if (command != "status" && !write) { Console.WriteLine("plan only; add --write"); return 0; }
        switch (command)
        {
            case "status": break;
            case "install": hook.Install(); break;
            case "on":
                {
                    int min = int.Parse(rest[1]), max = int.Parse(rest[2]);
                    int wonderkid = rest.Length > 3 ? int.Parse(rest[3]) : 0;
                    var scope = rest.Length > 4 ? Enum.Parse<GoldenGenerationScope>(rest[4], ignoreCase: true) : GoldenGenerationScope.All;
                    var ids = rest.Skip(5).Select(int.Parse).ToList();
                    hook.Configure(true, min, max, wonderkid, scope, ids);
                    break;
                }
            case "off":
                {
                    var s = hook.Read()?.Settings;
                    if (s is not null) hook.Configure(false, s.MinPa, s.MaxPa, s.WonderkidChance, s.Scope, s.Ids);
                    break;
                }
            case "uninstall": hook.Uninstall(); break;
            default: throw new ArgumentException($"unknown golden command '{command}'");
        }
        Print(session, hook.Read());
        return 0;
    }

    static void Print(GameSession session, GoldenGenerationState? state)
    {
        if (state is null) { Console.WriteLine("hook not installed"); return; }
        var s = state.Settings;
        Console.WriteLine($"hook in (block 0x{state.Cave:X}); regens seen {state.Calls}, PA raised {state.Raised}, wonderkids {state.Wonderkids}");
        Console.WriteLine($"{(s.Enabled ? "ON" : "off")}: PA {s.MinPa}-{s.MaxPa}, wonderkid {s.WonderkidChance}%, scope {s.Scope} {string.Join(",", s.Ids)}");
        if (state.Recent.Count == 0) return;
        var byAddress = session.Players.LoadAll().ToDictionary(p => p.Address);
        Console.WriteLine("last regens (newest first): player | rbx rdi r12 r13 r14 r15 club");
        foreach (var h in state.Recent)
        {
            string who = byAddress.TryGetValue(h.Player, out var p) ? $"{p.DisplayName} id {p.Id} CA {p.CurrentAbility} PA {p.PotentialAbility}" : $"0x{h.Player:X} (not in player list)";
            Console.WriteLine($"  {who} | {h.Rbx:X} {h.Rdi:X} {h.R12:X} {h.R13:X} {h.R14:X} {h.R15:X} club {h.Club:X}");
        }
    }
}
