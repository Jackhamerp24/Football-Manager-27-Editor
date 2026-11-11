using Fm27Editor.Core.Game;
using Fm27Editor.Core.Presets;

/// <summary>
/// <c>accelerate status</c> — where the training-gain code is, whether the patch is in, how often it ran, who is accelerated.
/// <c>accelerate set &lt;player id&gt; &lt;multiplier 2-20 | 0&gt; [--write]</c> — speed a player up (0 stops); installs on first use.
/// <c>accelerate clear [--write]</c> — empties the table. <c>accelerate uninstall [--write]</c> — puts the game's bytes back.
/// Without --write the write commands only print what they would do.
/// </summary>
static class AccelerateCli
{
    public static int Run(string[] args, Func<bool, GameSession> attach)
    {
        bool write = args.Contains("--write");
        var rest = args.Where(a => a != "--write").ToArray();
        string command = rest.FirstOrDefault() ?? "status";
        using var session = attach(write && command != "status");
        var accel = session.AcceleratedDevelopment;
        ulong site = accel.FindSite();
        Console.WriteLine($"build {session.Profile.Build}; site rva 0x{site - session.Game.ModuleBase:X}");

        switch (command)
        {
            case "status":
                Print(session, accel.Read());
                return 0;
            case "set":
                {
                    int multiplier = int.Parse(rest[^1]);
                    string who = string.Join(' ', rest[1..^1]);
                    var all = session.Players.LoadAll();
                    var matches = int.TryParse(who, out int id)
                        ? all.Where(p => p.Id == id).ToList()
                        : all.Where(p => p.DisplayName.Contains(who, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count != 1) throw new InvalidOperationException($"'{who}' matches {matches.Count} players; use the player id");
                    var player = matches[0];
                    Console.WriteLine($"player id {player.Id}");
                    Console.WriteLine($"{player.DisplayName} (CA {player.CurrentAbility}, PA {player.PotentialAbility}): multiplier {accel.MultiplierOf(player)} -> {multiplier}");
                    if (!write) { Console.WriteLine("plan only; add --write"); return 0; }
                    Console.WriteLine(accel.Set(player, multiplier) ? "written" : "already so");
                    Print(session, accel.Read());
                    return 0;
                }
            case "clear":
                {
                    var state = accel.Read();
                    Console.WriteLine($"would take out {state?.Players.Count ?? 0} player(s)");
                    if (!write || state is null) return 0;
                    foreach (var p in session.Players.LoadAll().Where(p => state.Players.Any(r => r.Address == p.Address)))
                        accel.Set(p, 0);
                    Console.WriteLine($"stale rows removed: {accel.RemoveStale()}");
                    Print(session, accel.Read());
                    return 0;
                }
            case "sample":
                {
                    // Polls the routine's ring for the given seconds and reports which kinds of players pass the site.
                    int seconds = rest.Length > 1 ? int.Parse(rest[1]) : 60;
                    var byAddress = session.Players.LoadAll().ToDictionary(p => p.Address);
                    var seen = new Dictionary<ulong, int>();
                    long callsStart = accel.Read()?.Calls ?? 0, callsEnd = callsStart;
                    var until = DateTime.UtcNow.AddSeconds(seconds);
                    while (DateTime.UtcNow < until)
                    {
                        var s = accel.Read();
                        if (s is null) break;
                        callsEnd = s.Calls;
                        foreach (var a in s.Recent) seen[a] = seen.GetValueOrDefault(a) + 1;
                        Thread.Sleep(20);
                    }
                    int players = seen.Keys.Count(byAddress.ContainsKey);
                    var known = seen.Keys.Where(byAddress.ContainsKey).Select(a => byAddress[a]).ToList();
                    int withClub = known.Count(p => p.Club is not null);
                    Console.WriteLine($"calls during sample: {callsEnd - callsStart}; distinct objects seen: {seen.Count}; players: {players}; with a club: {withClub}; without: {known.Count - withClub}");
                    foreach (var g in known.GroupBy(p => p.Age is { } age ? age / 5 * 5 : -1).OrderBy(g => g.Key))
                        Console.WriteLine($"  age {(g.Key < 0 ? "?" : $"{g.Key}-{g.Key + 4}")}: {g.Count()}");
                    foreach (var p in known.Where(p => p.Club is not null).Take(15))
                        Console.WriteLine($"  with club: {p.DisplayName} ({p.ClubName}) age {p.Age} CA {p.CurrentAbility} PA {p.PotentialAbility}");
                    return 0;
                }
            case "install":
                if (!write) { Console.WriteLine("plan only; add --write"); return 0; }
                Print(session, accel.Install());
                return 0;
            case "uninstall":
                if (!write) { Console.WriteLine("plan only; add --write"); return 0; }
                accel.Uninstall();
                Console.WriteLine(accel.Read() is null ? "original bytes restored" : "still patched");
                return 0;
            default:
                Console.Error.WriteLine("usage: accelerate status | set <player id> <multiplier> [--write] | clear [--write] | uninstall [--write]");
                return 2;
        }
    }

    static void Print(GameSession session, AcceleratedState? state)
    {
        if (state is null) { Console.WriteLine("not installed"); return; }
        Console.WriteLine($"installed: block 0x{state.Cave:X}; calls {state.Calls}; multiplied {state.Hits}; lift zero gains {state.LiftZeroGains}");
        var byAddress = session.Players.LoadAll().ToDictionary(p => p.Address);
        foreach (var row in state.Players)
        {
            string who = byAddress.TryGetValue(row.Address, out var p) ? $"{p.DisplayName} CA {p.CurrentAbility} PA {p.PotentialAbility}" : "(not a player now)";
            Console.WriteLine($"  id {row.PlayerId} x{row.Multiplier}  {who}");
        }
        if (state.Recent.Count > 0)
        {
            int known = state.Recent.Count(a => byAddress.ContainsKey(a));
            Console.WriteLine($"recent objects through the site: {state.Recent.Count}, of which players: {known}");
            foreach (var a in state.Recent)
                Console.WriteLine($"  0x{a:X}  {(byAddress.TryGetValue(a, out var p) ? $"{p.DisplayName} ({p.ClubName}) CA {p.CurrentAbility} PA {p.PotentialAbility}" : "(not a player in the list)")}");
        }
    }
}
