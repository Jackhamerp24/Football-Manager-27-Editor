using System.Diagnostics;
using Fm27Editor.Core.Discovery;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Verification;

// Football Manager 27 Editor command-line tools.
//   players [name]                     list players (filtered by name)
//   verify <golden.json>               compare decoded players with values copied from the game UI
//   set <name> <field> <value>         write one attribute / CA / PA / position / personality value
//   find-offsets <golden.json> <out>   rebuild the offset profile for the running build
string offsetsDir = Environment.GetEnvironmentVariable("FM27_OFFSETS") ?? Path.Combine(AppContext.BaseDirectory, "offsets");
Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    return args.FirstOrDefault() switch
    {
        "players" => Players(args.Length > 1 ? string.Join(' ', args[1..]) : null),
        "verify" => Verify(args[1]),
        "set" => Set(args[1], args[2], int.Parse(args[3])),
        "find-offsets" => FindOffsets(args[1], args[2], args.SkipWhile(a => a != "--date").Skip(1).FirstOrDefault()),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is GameNotRunningException or UnsupportedBuildException or StaleObjectException or DiscoveryException or MemoryAccessException or ArgumentOutOfRangeException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

int Usage()
{
    Console.Error.WriteLine("usage: players [name] | verify <golden.json> | set <name> <field> <value> | find-offsets <golden.json> <out.json> [--date yyyy-MM-dd]");
    return 1;
}

GameSession Attach(bool writable) => GameSession.Attach(GameSession.LoadProfiles(offsetsDir), writable);

int Players(string? filter)
{
    using var session = Attach(writable: false);
    var sw = Stopwatch.StartNew();
    var players = session.Players.LoadAll();
    var today = session.ReadGameDate();
    Console.WriteLine($"build {session.Profile.Build}: {players.Count} players in {sw.ElapsedMilliseconds} ms, game date {today?.ToString("yyyy-MM-dd") ?? "unknown"}");
    var shown = filter is null ? players.Take(20) : players.Where(p => Matches(p, filter));
    foreach (var p in shown)
        Console.WriteLine($"{p.DisplayName,-30} age {(today is { } t ? p.AgeOn(t) : null),-3} {p.Club?.DisplayName,-22} £{p.Contract?.Wage,-7} to {p.Contract?.Expires} joined {p.Contract?.Joined}  " +
                          $"CA {p.CurrentAbility,3} PA {p.PotentialAbility,3}  {p.Height} cm  {p.DateOfBirth}  " +
                          $"Cro {p.Attribute(PlayerAttribute.Crossing)} Dri {p.Attribute(PlayerAttribute.Dribbling)} Pac {p.Attribute(PlayerAttribute.Pace)}  0x{p.Address:X}");
    return 0;
}

static bool Matches(PlayerRecord p, string filter) =>
    new[] { p.DisplayName, p.FullName }.Any(n => n?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);

int Verify(string goldenPath)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    int failed = 0;
    foreach (var g in GoldenPlayers.Load(goldenPath))
    {
        var candidates = players.Where(p => p.DisplayName == g.Name && (g.DateOfBirth is null || p.DateOfBirth == g.DateOfBirth)).ToList();
        if (candidates.Count != 1)
        {
            Console.WriteLine($"FAIL {g.Name}: {candidates.Count} players with this name and birth date");
            failed++;
            continue;
        }
        var diffs = GoldenPlayers.Compare(g, candidates[0]);
        Console.WriteLine(diffs.Count == 0
            ? $"ok   {g.Name} ({g.Attributes.Count} attributes, CA {candidates[0].CurrentAbility} PA {candidates[0].PotentialAbility})"
            : $"FAIL {g.Name}: {string.Join("; ", diffs)}");
        if (diffs.Count > 0) failed++;
    }
    Console.WriteLine(failed == 0 ? "all golden players match" : $"{failed} golden players differ");
    return failed == 0 ? 0 : 3;
}

int Set(string name, string field, int value)
{
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
    {
        Console.Error.WriteLine($"{matches.Count} players named '{name}'");
        return 2;
    }
    var p = matches[0];
    var w = session.Writer;
    string before;
    if (Enum.TryParse<PlayerAttribute>(field, true, out var a)) { before = p.Attribute(a).ToString(); w.SetAttribute(p, a, value); }
    else if (Enum.TryParse<PlayerPosition>(field, true, out var pos)) { before = p.Position(pos).ToString(); w.SetPosition(p, pos, value); }
    else if (Enum.TryParse<PersonalityAttribute>(field, true, out var per)) { before = p.PersonalityValue(per).ToString(); w.SetPersonality(p, per, value); }
    else if (field.Equals("CA", StringComparison.OrdinalIgnoreCase)) { before = p.CurrentAbility.ToString(); w.SetCurrentAbility(p, value); }
    else if (field.Equals("PA", StringComparison.OrdinalIgnoreCase)) { before = p.PotentialAbility.ToString(); w.SetPotentialAbility(p, value); }
    else
    {
        Console.Error.WriteLine($"unknown field '{field}'");
        return 2;
    }
    Console.WriteLine($"{p.DisplayName} {field}: {before} -> {value}");
    return 0;
}

int FindOffsets(string goldenPath, string outPath, string? gameDate)
{
    var game = GameProcess.Locate();
    using var memory = new WindowsProcessMemory(game.ProcessId, writable: false);
    var sw = Stopwatch.StartNew();
    var date = gameDate is null ? (DateOnly?)null : DateOnly.Parse(gameDate, System.Globalization.CultureInfo.InvariantCulture);
    var profile = new OffsetFinder(memory, game, Console.WriteLine).Find(GoldenPlayers.Load(goldenPath), date);
    File.WriteAllText(outPath, profile.ToJson());
    Console.WriteLine($"wrote {outPath} in {sw.Elapsed.TotalSeconds:F0} s");
    return 0;
}
