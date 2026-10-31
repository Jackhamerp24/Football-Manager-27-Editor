using System.Diagnostics;
using Fm27Editor.Core.Discovery;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;
using Fm27Editor.Core.Verification;

// Football Manager 27 Editor command-line tools.
//   players [name]                     list players (filtered by name)
//   verify <golden.json>               compare decoded players with values copied from the game UI
//   set <name> <field> <value>         write one attribute / CA / PA / position / personality value,
//                                      Height (cm) or Born (yyyy-MM-dd)
//   find-offsets <golden.json> <out>   rebuild the offset profile for the running build
//   transfer dump <name> [out.json]    snapshot player/contract/pending/club/finance bytes
//   transfer peek <name>               print the transfer-relevant fields of one player
//   transfer diff <before> <after>     compare two snapshots by object offset
string offsetsDir = Environment.GetEnvironmentVariable("FM27_OFFSETS") ?? Path.Combine(AppContext.BaseDirectory, "offsets");
Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    return args.FirstOrDefault() switch
    {
        "players" => Players(args.Length > 1 ? string.Join(' ', args[1..]) : null),
        "verify" => Verify(args[1]),
        "set" => Set(args[1], args[2], args[3]),
        "move" => Move(args[1], int.Parse(args[2]), int.Parse(args[3]), args.Contains("--write")),
        "contract" => ContractCommand(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null),
        "find-offsets" => FindOffsets(args[1], args[2], args.SkipWhile(a => a != "--date").Skip(1).FirstOrDefault()),
        "transfer" => Transfer(args[1..]),
        "clauses" => args[1] == "fee" ? SellOnsWithFee() : Clauses(int.Parse(args[1])),
        "club" => ClubInfoCommand(int.Parse(args[1])),
        "clubfields" => ClubFieldsCommand(int.Parse(args[1])),
        "setclub" => SetClubField(int.Parse(args[1]), args[2], long.Parse(args[3])),
        "clublists" => ClubListsCommand(int.Parse(args[1])),
        "clubfinancelists" => ClubFinanceListsCommand(int.Parse(args[1])),
        "probe" => Probe(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16), int.Parse(args[3]), args[4..]),
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
    Console.Error.WriteLine("usage: players [name] | verify <golden.json> | set <name> <field> <value> | find-offsets <golden.json> <out.json> [--date yyyy-MM-dd] | transfer dump|peek <name> [out.json] | transfer diff <before.json> <after.json>");
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
        Console.WriteLine($"{p.DisplayName,-30} age {(today is { } t ? p.AgeOn(t) : null),-3} {p.Club?.DisplayName,-22} [{p.NationalityName,-12} | {p.DivisionName,-18}] £{p.Contract?.Wage,-7} to {p.Contract?.Expires} joined {p.Contract?.Joined}  " +
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

// move <name> <buyer club id> <fee> [--write]   permanent transfer; without --write only prints the plan
int Move(string name, int buyerId, int fee, bool write)
{
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var matches = all.Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var sw = Stopwatch.StartNew();
    var clubs = session.LoadClubs(all);
    Console.WriteLine($"{clubs.Count} clubs, {clubs.Sum(c => c.Teams.Count)} teams in {sw.ElapsedMilliseconds} ms");
    var buyer = clubs.SingleOrDefault(c => c.Id == buyerId);
    if (buyer is null) { Console.Error.WriteLine($"no club with id {buyerId}"); return 2; }
    var plan = session.Transfers.Plan(matches[0], clubs, buyer, fee);
    Console.WriteLine($"{matches[0].DisplayName}: {plan.Seller.DisplayName} -> {plan.Buyer.DisplayName} for {fee}, wage {plan.Wage}");
    Console.WriteLine($"into team 0x{plan.ToTeam.Address:X} ({plan.ToTeam.SquadCount}/{plan.ToTeam.SquadCapacity}{(plan.ToTeam.IsFirstTeam ? ", first team" : "")})"
                      + (plan.NeedsAllocation ? ", squad full: a new buffer will be allocated" : ""));
    foreach (var w in plan.Writes.Where(w => w.Value.Length == 4))
        Console.WriteLine($"  {w.What}: {BitConverter.ToInt32(w.Expected)} -> {BitConverter.ToInt32(w.Value)}");
    Console.WriteLine($"  {plan.Writes.Count(w => w.Value.Length == 8)} pointer writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    session.ApplyTransfer(plan);
    var after = session.Players.Reload(matches[0].Address)!;
    Console.WriteLine($"applied; contract club now {after.Club?.DisplayName}");
    return 0;
}

// clauses <club id>   sell-on / appearance fee / competition bonus clauses on the club's players
int Clauses(int clubId)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var club = session.LoadClubs(all).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var byId = all.Where(p => p.Id is not null).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.First());
    var clauses = session.ReadTransferClauses(club.Address, session.LoadCompetitions(all));
    Console.WriteLine($"{club.DisplayName}: {clauses.Count} clauses");
    foreach (var c in clauses)
        Console.WriteLine($"  {c.Kind,-16} {(byId.TryGetValue(c.PlayerId, out var p) ? p.DisplayName : c.PlayerId.ToString()),-24} {c.Beneficiary?.DisplayName,-24} " +
                          $"fee {c.Amount?.ToString() ?? "-"} pct {c.Percentage?.ToString() ?? "-"} signed for {c.SignedFor?.ToString() ?? "-"} apps {c.AppearancesSoFar}/{c.Appearances} competition {c.Competition?.DisplayName ?? c.CompetitionIndex?.ToString()} ends {c.Ends:yyyy-MM-dd}");
    return 0;
}

// clubfinancelists <id>   Finances sponsors and debts read from the live game
int ClubFinanceListsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var sponsors = session.ReadSponsors(club.Address);
    Console.WriteLine($"{club.Name}: {sponsors.Count} sponsors");
    foreach (var s in sponsors) Console.WriteLine($"  type {s.IncomeTypeCode,3} {s.TotalValue,12:N0} {s.StartDate:yyyy-MM-dd} {s.EndDate:yyyy-MM-dd}");
    var debts = session.ReadDebts(club.Address);
    Console.WriteLine($"{debts.Count} debts");
    foreach (var d in debts) Console.WriteLine($"  source {d.SourceCode,3} {d.OriginalDebt,12:N0} {d.StartDate:yyyy-MM-dd} {d.EndDate:yyyy-MM-dd}");
    return 0;
}

// setclub <id> <ClubField> <value>   guarded write of one mapped club field (team fields go to the first team)
int SetClubField(int clubId, string fieldName, long value)
{
    var field = Enum.Parse<Fm27Editor.Core.Clubs.ClubField>(fieldName, ignoreCase: true);
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var access = session.ClubFields;
    var root = session.Profile.Club!.Fields![field].Root == Fm27Editor.Core.Clubs.ClubFieldRoot.Club ? club.Address : club.FirstTeam?.Address ?? 0;
    var before = access.Read(root, field) ?? throw new InvalidOperationException($"{field} is not readable for {club.Name}");
    access.Set(root, field, before, value);
    Console.WriteLine($"{club.Name} {field}: {before} -> {access.Read(root, field)}");
    return 0;
}

// clubfields <id>   every club.fields mapping read from the live game (club fields + first team)
int ClubListsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var vision = session.ReadClubVision(club.FirstTeam?.Address ?? 0, clubs);
    var affiliations = session.ReadAffiliations(club.Address);
    var regional = session.ReadRegionalDivisions(club.Address);
    Console.WriteLine($"{club.Name} id {clubId}: confirmed culture {vision.Count}, affiliations {affiliations.Count}, regional {regional.Count}");
    foreach (var r in vision)
        Console.WriteLine($"  vision type {r.TypeCode} importance {r.Importance} start {r.StartDate:yyyy-MM-dd} end {r.EndDate:yyyy-MM-dd} item {r.ItemIndex} {r.ClubItem?.DisplayName}");
    foreach (var r in affiliations)
        Console.WriteLine($"  affiliation {r.ParentClub?.DisplayName} -> {r.FeederClub?.DisplayName} type {r.TypeCode} start {r.StartDate:yyyy-MM-dd}");
    foreach (var r in regional) Console.WriteLine($"  regional level {r.Level}");
    return 0;
}

int ClubFieldsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var values = session.ClubFields.ReadAll(club.Address, club.FirstTeam?.Address ?? 0);
    Console.WriteLine($"{club.Name}: {values.Count} mapped fields");
    foreach (var (f, v) in values.OrderBy(kv => kv.Key)) Console.WriteLine($"  {f,-24} {v}");
    return 0;
}

// club <id>   addresses of the club object, its finance object, clause holder and teams (read only)
int ClubInfoCommand(int clubId)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var club = session.LoadClubs(all).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var k = session.Profile.Club!;
    Console.WriteLine($"{club.Name} ({club.ShortName}) id {club.Id}");
    Console.WriteLine($"  club    0x{club.Address:X}  vtable +0x{k.ClubVtableRva:X}");
    Console.WriteLine($"  finance 0x{club.Finance:X}  (club+0x{k.Finance?.Pointer:X})");
    if (k.TransferClauses is { } tc) Console.WriteLine($"  clauses 0x{session.Memory.ReadUInt64(club.Address + (ulong)tc.Pointer):X}  (club+0x{tc.Pointer:X})");
    foreach (var t in club.Teams)
        Console.WriteLine($"  team    0x{t.Address:X}  {(t.IsFirstTeam ? "first team" : "")} squad {t.SquadCount}/{t.SquadCapacity}");
    Console.WriteLine($"  module  0x{session.ModuleBase:X}");
    return 0;
}

// probe <hexAddr> <hexSize> <depth> <value>...
//   Read-only search for values inside [addr, addr+size) and, when depth > 0, inside the first 0x1000 bytes of every
//   object a qword in that range points to (depth levels). Each value is matched as int32, int16 and (when 0-255) byte.
//   Values may be decimal or 0x-hex. Prints path as +off[->+off...] so the field can be read back as a chain.
int Probe(ulong address, int size, int depth, string[] values)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var wants = values.Select(v => v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(v[2..], 16) : long.Parse(v)).ToArray();
    var seen = new HashSet<ulong>();
    void Scan(ulong at, int length, string path, int level)
    {
        if (!seen.Add(at)) return;
        var b = new byte[length];
        if (!mem.TryRead(at, b))
        {
            // Fall back to the largest readable prefix in 0x100 steps.
            int ok = 0;
            for (int n = 0x100; n <= length; n += 0x100) { if (mem.TryRead(at, b.AsSpan(0, n))) ok = n; else break; }
            if (ok == 0) return;
            length = ok;
        }
        for (int o = 0; o + 1 < length; o++)
            foreach (var w in wants)
            {
                if (o % 4 == 0 && o + 4 <= length && BitConverter.ToInt32(b, o) == w) Console.WriteLine($"{path}+0x{o:X} int32 = {w}");
                else if (o % 2 == 0 && w is >= short.MinValue and <= short.MaxValue && BitConverter.ToInt16(b, o) == w) Console.WriteLine($"{path}+0x{o:X} int16 = {w}");
                // Single bytes match almost everywhere, so they are reported only when one value is asked for.
                else if (wants.Length == 1 && w is >= 0 and <= 255 && b[o] == w) Console.WriteLine($"{path}+0x{o:X} byte = {w}");
            }
        if (level <= 0) return;
        for (int o = 0; o + 8 <= length; o += 8)
        {
            ulong q = BitConverter.ToUInt64(b, o);
            if (q < 0x10000 || q > 0x7FFF_FFFF_FFFF || q >= session.ModuleBase && q < session.ModuleBase + 0x1000_0000) continue;
            Scan(q, 0x1000, $"{path}+0x{o:X}->", level - 1);
        }
    }
    Scan(address, size, "", depth);
    return 0;
}

// clauses fee   every sell-on that stores the signed-for fee (not -1), with player and both clubs
int SellOnsWithFee()
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var byId = all.Where(p => p.Id is not null).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.First());
    foreach (var club in session.LoadClubs(all))
        foreach (var c in session.ReadTransferClauses(club.Address).Where(c => c.Kind == Fm27Editor.Core.Clubs.TransferClauseKind.SellOn && c.SignedFor is not null))
            Console.WriteLine($"{(byId.TryGetValue(c.PlayerId, out var p) ? p.DisplayName : c.PlayerId.ToString()),-26} at {club.DisplayName,-24} {c.Percentage}% to {c.Beneficiary?.DisplayName,-24} signed for {c.SignedFor:N0}");
    return 0;
}

// contract <name>                      show wage, expiry, playing time
// contract <name> Wage|Expires|PlayingTime <value>
int ContractCommand(string name, string? field, string? text)
{
    using var session = Attach(writable: field is not null);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var p = matches[0];
    var w = new ContractWriter(session.Memory, session.Profile, session.ModuleBase);
    if (p.Contract is not { } c) { Console.Error.WriteLine("no contract"); return 2; }
    var d = w.Read(p)!;
    Console.WriteLine($"{p.DisplayName}: {d.Type}, wage {d.Wage}, loyalty {d.LoyaltyBonus}, number {d.SquadNumber}");
    Console.WriteLine($"  started {d.Started:yyyy-MM-dd} signed {d.Signed:yyyy-MM-dd} expires {d.Expires:yyyy-MM-dd} joined club {d.JoinedClub:yyyy-MM-dd}");
    Console.WriteLine($"  playing time {d.PlayingTime}, future {d.FuturePlayingTime} from {d.FuturePlayingTimeYear}, happiness {d.Happiness}, playing time happiness {d.PlayingTimeHappiness}");
    foreach (var t in d.Terms) Console.WriteLine($"  term {t.Index}: type {t.Type} value {t.Value} parameter {t.Parameter}");
    if (field is null) return 0;
    switch (field.ToLowerInvariant())
    {
        case "wage": w.SetWage(p, int.Parse(text!)); break;
        case "expires": w.SetExpiry(p, DateOnly.ParseExact(text!, "yyyy-MM-dd")); break;
        case "playingtime": w.SetPlayingTime(p, Enum.Parse<PlayingTime>(text!, true)); break;
        default: Console.Error.WriteLine($"unknown contract field '{field}'"); return 2;
    }
    var after = session.Players.Reload(p.Address)!;
    Console.WriteLine($"after: wage {after.Contract?.Wage}, expires {after.Contract?.Expires:yyyy-MM-dd}, playing time {w.ReadPlayingTime(after)}");
    return 0;
}

int Set(string name, string field, string text)
{
    bool born = field.Equals("Born", StringComparison.OrdinalIgnoreCase);
    DateOnly date = default;
    int value = 0;
    if (born ? !DateOnly.TryParseExact(text, "yyyy-MM-dd", out date) : !int.TryParse(text, out value))
    {
        Console.Error.WriteLine($"bad value '{text}' for {field}");
        return 2;
    }
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
    else if (field.Equals("Height", StringComparison.OrdinalIgnoreCase)) { before = p.Height.ToString(); w.SetHeight(p, value); }
    else if (born) { before = $"{p.DateOfBirth:yyyy-MM-dd}"; w.SetDateOfBirth(p, date); }
    else
    {
        Console.Error.WriteLine($"unknown field '{field}'");
        return 2;
    }
    Console.WriteLine($"{p.DisplayName} {field}: {before} -> {text}");
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

int Transfer(string[] args)
{
    string sub = args.FirstOrDefault() ?? "";
    try
    {
        return sub switch
        {
            "dump" => TransferDump(args[1], args.ElementAtOrDefault(2)),
            "peek" => TransferPeek(args[1]),
            "offers" => TransferOffers(),
            "diff" => TransferShowDiff(args[1], args[2]),
            _ => Usage(),
        };
    }
    catch (IndexOutOfRangeException) { return Usage(); }
}

PlayerRecord FindOne(GameSession session, string name)
{
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) throw new ArgumentOutOfRangeException(nameof(name), name, $"{matches.Count} players named '{name}'");
    return matches[0];
}

int TransferPeek(string name)
{
    using var session = Attach(writable: false);
    var player = FindOne(session, name);
    Console.WriteLine($"{player.DisplayName}  player 0x{player.Address:X}  game date {session.ReadGameDate()?.ToString("yyyy-MM-dd") ?? "unknown"}");
    Console.WriteLine($"club {player.Club?.DisplayName ?? "-"}  wage {player.Contract?.Wage.ToString() ?? "-"}  expires {player.Contract?.Expires?.ToString("yyyy-MM-dd") ?? "-"}  joined {player.Contract?.Joined?.ToString("yyyy-MM-dd") ?? "-"}");
    Console.WriteLine($"contract +0x330 -> 0x{player.Contract?.Address ?? 0:X}   pending +0x328 -> 0x{player.PendingContractAddress:X}");
    foreach (var clause in session.ReadClauses(player))
        Console.WriteLine($"clause type {clause.Type,3}  value {clause.Value}");
    if (player.Club is { } club) PrintFinance(session.ReadFinance(club.Address));
    return 0;
}

void PrintFinance(ClubFinance? finance)
{
    if (finance is null) { Console.WriteLine("finance unknown"); return; }
    Console.WriteLine($"finance 0x{finance.Address:X}  balance {finance.Balance}  budget {finance.TransferBudget}  spent {finance.SpentThisSeason}  wages {finance.WageSpend}");
}

int TransferOffers()
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    var withOffer = players.Where(p => p.PendingContractAddress != 0).ToList();
    Console.WriteLine($"{withOffer.Count} players have a pending contract pointer (player+0x328)");
    foreach (var player in withOffer.Take(40))
        Console.WriteLine($"{player.DisplayName,-28} club {player.Club?.DisplayName ?? "-",-24} contract 0x{player.Contract?.Address ?? 0:X} pending 0x{player.PendingContractAddress:X} clauses {session.ReadClauses(player).Count}");
    return 0;
}

int TransferDump(string name, string? outPath)
{
    using var session = Attach(writable: false);
    var player = FindOne(session, name);
    var snapshot = TransferSnapshot.Capture(session, player);
    string path = outPath ?? $"transfer-{string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '-'))}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    snapshot.Save(path);
    Console.WriteLine($"{player.DisplayName}: saved {path}  ({snapshot.Objects.Count} objects, {snapshot.Clauses.Count} clauses)");
    Console.WriteLine($"club {snapshot.Club ?? "-"}  wage {snapshot.Wage?.ToString() ?? "-"}  expires {snapshot.Expires ?? "-"}  joined {snapshot.Joined ?? "-"}");
    return 0;
}

int TransferShowDiff(string beforePath, string afterPath)
{
    var before = TransferSnapshot.Load(beforePath);
    var after = TransferSnapshot.Load(afterPath);
    Console.WriteLine($"{before.PlayerName}: {before.CapturedAt} -> {after.CapturedAt}");
    var changes = TransferDiff.Compare(before, after);
    foreach (var c in changes)
    {
        string where = c.Name is null ? $"{c.Section,-8} +0x{c.Offset:X3}" : $"{c.Section,-8} {c.Name}";
        string values = c.PointerBefore is { } pb
            ? $"ptr 0x{pb:X} -> 0x{c.PointerAfter ?? 0:X}"
            : $"{c.IntBefore?.ToString() ?? c.Before} -> {c.IntAfter?.ToString() ?? c.After}";
        Console.WriteLine($"{where}  {values}");
    }
    if (changes.Count == 0) Console.WriteLine("no differences");
    return 0;
}
