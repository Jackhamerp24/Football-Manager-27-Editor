using System.Globalization;
using System.Text.Json;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Memory;

/// <summary>
/// Calibration-save tooling: resolve the entities named by calibration/manifest.json on the attached
/// game and report where they live. Marker scanning and expected-value checks build on this.
/// </summary>
public static class Calibration
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 && !(args.Length == 1 && args[0] == "clausetypes")) { Console.WriteLine("usage: calibrate entities|verify <manifest.json> | dump player|club|nation <name> | unhappiness <hexOffset>"); return 2; }
        return args[0] switch
        {
            "entities" => Entities(args[1]),
            "verify" => Verify(args[1]),
            "unhappiness" => UnhappinessSurvey(args[1]),
            "clausetypes" => ClauseTypeSurvey(),
            "staffraw" when args.Length == 2 => StaffRawAll(args[1]),
            "staffraw" when args.Length >= 4 => StaffRaw(args[1], Enum.Parse<StaffAttribute>(args[2]), Convert.ToByte(args[3])),
            "dump" when args.Length >= 3 => Dump(args[1], string.Join(' ', args[2..])),
            _ => Unknown(args[0])
        };
    }

    static int Unknown(string command)
    {
        Console.WriteLine($"unknown calibrate command '{command}'; expected: entities, verify");
        return 2;
    }

    static int Entities(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entities = document.RootElement.GetProperty("entities");
        var profiles = GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets"));
        using var session = GameSession.Attach(profiles, false);
        int failures = 0;

        ulong? Vtable(ulong address)
        {
            var bytes = new byte[8];
            return session.Memory.TryRead(address, bytes) ? BitConverter.ToUInt64(bytes) : null;
        }

        void Check(string role, int id, ulong? address, string? name)
        {
            if (address is not { } at) { Console.WriteLine($"MISS  {role,-16} id={id}"); failures++; return; }
            ulong? vtable = Vtable(at);
            string rva = vtable is { } vt && vt >= session.ModuleBase ? $"0x{vt - session.ModuleBase:X}" : "?";
            Console.WriteLine($"ok    {role,-16} id={id,-12} name={name,-36} address=0x{at:X} vtable={rva}");
        }

        if (entities.TryGetProperty("player", out var playerId))
        {
            int id = playerId.GetInt32();
            var player = session.Players.LoadAll().FirstOrDefault(p => p.Id == id);
            Check("player", id, player?.Address, player?.DisplayName);
        }
        if (entities.TryGetProperty("staff", out var staffId))
        {
            int id = staffId.GetInt32();
            var staff = session.Staff.LoadAll().FirstOrDefault(s => s.Id == id);
            Check("staff", id, staff?.Address, staff?.DisplayName);
        }
        if (entities.TryGetProperty("club", out var clubId))
        {
            int id = clubId.GetInt32();
            var club = session.ScanAllClubs().FirstOrDefault(c => c.Id == id);
            Check("club", id, club?.Address, club?.Name);
        }
        if (entities.TryGetProperty("partnerClub", out var partnerId))
        {
            int id = partnerId.GetInt32();
            var club = session.ScanAllClubs().FirstOrDefault(c => c.Id == id);
            Check("partnerClub", id, club?.Address, club?.Name);
        }
        if (entities.TryGetProperty("nation", out var nationId))
        {
            int id = nationId.GetInt32();
            var nation = session.Nations.LoadAll().FirstOrDefault(n => n.Id == id);
            Check("nation", id, nation?.Address, nation?.Name);
        }
        if (entities.TryGetProperty("competition", out var competitionId))
        {
            int id = competitionId.GetInt32();
            var competition = new CompetitionRepository(session.Memory, session.Profile, session.ModuleBase)
                .LoadAll().FirstOrDefault(c => c.Id == id);
            Check("competition", id, competition?.Address, competition?.DisplayName);
        }
        if (entities.TryGetProperty("stadium", out var stadiumId))
        {
            int id = stadiumId.GetInt32();
            var stadium = session.Stadiums.LoadAll().FirstOrDefault(x => x.Id == id);
            Check("stadium", id, stadium?.Address, stadium?.Name);
        }
        if (entities.TryGetProperty("city", out var cityId))
        {
            int id = cityId.GetInt32();
            var city = session.Cities.LoadAll().FirstOrDefault(x => x.Id == id);
            Check("city", id, city?.Address, city?.Name);
        }
        if (entities.TryGetProperty("award", out var awardId))
        {
            int id = awardId.GetInt32();
            var award = session.Awards.LoadAll().FirstOrDefault(x => x.Id == id);
            Check("award", id, award?.Address, award?.Name ?? award?.ShortName);
        }

        Console.WriteLine(failures == 0 ? "all manifest entities found" : $"{failures} manifest entities missing");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Checks every anchor in the manifest against the live save: the single-value checks, the club relationship
    /// rows and the transfer clauses. Fields the editor does not read yet are reported as SKIP, never as a pass.
    /// </summary>
    static int Verify(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var profiles = GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets"));
        using var session = GameSession.Attach(profiles, false);

        var players = session.Players.LoadAll();
        var staffAll = session.Staff.LoadAll();
        var clubs = session.ScanAllClubs();
        var nations = session.Nations.LoadAll();

        int Id(string role)
        {
            if (root.TryGetProperty("entities", out var e) && e.TryGetProperty(role, out var v)) return v.GetInt32();
            if (root.TryGetProperty("extra", out var x) && x.TryGetProperty(role, out var w)) return w.GetInt32();
            throw new InvalidOperationException($"manifest has no id for '{role}'");
        }

        var player = players.First(p => p.Id == Id("player"));
        var staff = staffAll.First(s => s.Id == Id("staff"));
        var man = clubs.First(c => c.Id == Id("club"));
        var chester = clubs.First(c => c.Id == Id("chesterfield"));
        var nation = nations.First(n => n.Id == Id("nation"));
        var contract = session.Contracts.Read(player);
        var staffContract = session.StaffContracts.Read(staff);
        var playerFields = session.PlayerFields.ReadAll(player);
        var manFields = session.ClubFields.ReadAll(man.Address, man.FirstTeam!.Address);
        var chesterFields = session.ClubFields.ReadAll(chester.Address, chester.FirstTeam!.Address);
        var nationFields = session.NationFields.ReadAll(nation.Address);
        var capital = session.NationLinks.Capital(nation.Address);
        int pass = 0, fail = 0, warn = 0, skip = 0;

        static string Text(object? value) => value switch
        {
            null => "",
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        // Manifest values are copied verbatim from FMRTE (M/D/YYYY dates, "£1,000", true/false).
        static bool Same(string expected, string actual) => SameNormalized(Fm27Editor.Core.Calibration.FmrteValue.Normalize(expected), actual);

        static bool SameNormalized(string expected, string actual) =>
            long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out long e) &&
            long.TryParse(actual, NumberStyles.Integer, CultureInfo.InvariantCulture, out long a)
                ? e == a
                : string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

        void Report(string what, string expected, string actual, string? note = null)
        {
            if (note is not null)
            {
                Console.WriteLine($"SKIP  {what}: {note}");
                skip++;
            }
            else if (Same(expected, actual))
            {
                Console.WriteLine($"PASS  {what}: {actual}");
                pass++;
            }
            else
            {
                Console.WriteLine($"FAIL  {what}: actual={actual} expected={expected}");
                fail++;
            }
        }

        void Warn(string what, string expected, string actual, string note)
        {
            Console.WriteLine($"WARN  {what}: actual={actual} expected={expected} ({note})");
            warn++;
        }

        ContractTerm? Term(short type) => contract?.Terms.FirstOrDefault(t => t.Type == type);

        (string Actual, string? Note) Actual(string entity, string field)
        {
            string? NoReader(string why) => why;
            if (entity is "player")
            {
                switch (field)
                {
                    case "CurrentAbility": return (Text(player.CurrentAbility), null);
                    case "PotentialAbility": return (Text(player.PotentialAbility), null);
                    case "Wage": return (Text(contract?.Wage), contract is null ? "no contract" : null);
                    case "Expires": return (Text(contract?.Expires), contract is null ? "no contract" : null);
                    case "Signed": return (Text(contract?.Signed), contract is null ? "no contract" : null);
                    case "Started": return (Text(contract?.Started), contract is null ? "no contract" : null);
                    case "Height": return (Text(player.Height), null);
                    case "Caps": return playerFields.TryGetValue(PlayerField.Caps, out var caps) ? (Text(caps), null) : (Text(player.InternationalCaps), null);
                    case "Born": return (Text(player.DateOfBirth), null);
                    case "Club": return (Text(player.Club?.Name), null);
                    case "AskingPrice": return (Text(player.AskingPrice), null);
                    case "AppearanceFee": return Term(32) is { } f ? (Text(f.Value), null) : ("", "no term 32");
                    case "GoalBonus": return Term(33) is { } g ? (Text(g.Value), null) : ("", "no term 33");
                    case "AssistBonus": return Term(39) is { } a ? (Text(a.Value), null) : ("", "no term 39");
                    case "UnusedSubstituteFee": return Term(38) is { } u ? (Text(u.Value), null) : ("", "no term 38");
                    case "RelegationWageDropPercentage": return Term(15) is { } r ? (Text(r.Parameter), null) : ("", "no term 15");
                }
            }
            if (entity is "staff")
            {
                switch (field)
                {
                    case "CurrentAbility": return (Text(staff.CurrentAbility), null);
                    case "PotentialAbility": return (Text(staff.PotentialAbility), null);
                    case "Wage": return (Text(staffContract?.Wage), staffContract is null ? "no contract" : null);
                    case "Expires": return (Text(staffContract?.Expires), staffContract is null ? "no contract" : null);
                    case "Started": return (Text(staffContract?.Started), staffContract is null ? "no contract" : null);
                    case "Born": return (Text(staff.DateOfBirth), null);
                    case "CityOfBirth": return ("", "no reader for staff city of birth on either build");
                    case "HomeReputation": return (Text(staff.HomeReputation), null);
                    case "CurrentReputation": return (Text(staff.CurrentReputation), null);
                    case "WorldReputation": return (Text(staff.WorldReputation), null);
                }
            }
            if (entity is "club" or "chesterfield")
            {
                var entry = entity == "club" ? man : chester;
                var fields = entity == "club" ? manFields : chesterFields;
                if (field == "Name") return (Text(entry.Name), null);
                if (Enum.TryParse<ClubField>(field, true, out var cf))
                    return fields.TryGetValue(cf, out var value) ? (Text(value), null) : ("", $"club field {cf} is not mapped");
                return ("", NoReader($"no club field '{field}'")!);
            }
            if (entity is "nation")
            {
                if (field == "Name") return (Text(nation.DisplayName), null);
                if (field == "Capital") return (Text(capital?.Name), capital is null ? "no capital city" : null);
                if (Enum.TryParse<NationField>(field, true, out var nf))
                    return nationFields.TryGetValue(nf, out var value) ? (Text(value), null) : ("", $"nation field {nf} is not mapped");
                return ("", NoReader($"no nation field '{field}'")!);
            }
            return ("", $"unknown entity '{entity}'");
        }

        if (root.TryGetProperty("checks", out var checks))
            foreach (var check in checks.EnumerateArray())
            {
                string entity = check.GetProperty("entity").GetString() ?? "";
                string field = check.GetProperty("field").GetString() ?? "";
                var expected = check.GetProperty("value");
                string shown = $"{entity}.{field}";
                var (actual, note) = Actual(entity, field);
                Report(shown, expected.ValueKind == JsonValueKind.Number ? expected.GetRawText() : expected.GetString() ?? "", actual, note);
            }

        static string FoldName(string? value) => new string((value ?? "").Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Replace('ı', 'i').Replace('ø', 'o').Replace('ł', 'l');

        bool NameIs(string? actual, string expected)
        {
            string a = FoldName(actual), e = FoldName(expected);
            return a.Length > 0 && (a == e || a.Contains(e, StringComparison.Ordinal) || e.Contains(a, StringComparison.Ordinal));
        }

        // The manifest uses FMRTE's display names; the game stores short names on Game Pass ("R. Madrid" for
        // Real Madrid, "Bergamo" for Atalanta). These aliases are for verification only, never for the editor.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Real Madrid"] = "R. Madrid",
            ["Sporting Clube de Portugal"] = "Sporting CP",
            ["Atalanta B.C."] = "Bergamo",
            ["F.C. Internazionale Milano"] = "Blu-neri Milano",
            ["Lille Olympique Sporting Club"] = "LOSC",
            ["Wolverhampton Wanderers"] = "Wolves",
            ["Manchester City"] = "Man City",
        };
        bool ClubNameIs(string expected, string? name, string? shortName) =>
            NameIs(name, expected) || NameIs(shortName, expected) ||
            (aliases.TryGetValue(expected, out var alias) && (NameIs(name, alias) || NameIs(shortName, alias)));

        if (root.TryGetProperty("relationships", out var relationships))
            foreach (var relationship in relationships.EnumerateArray())
            {
                int clubId = relationship.GetProperty("club").GetInt32();
                var entry = clubId == man.Id ? man : chester;
                string type = relationship.GetProperty("type").GetString() ?? "";
                var kind = type switch
                {
                    "Rival" => ClubRelationshipKind.Rival,
                    "Likes Person" => ClubRelationshipKind.LikesPerson,
                    "Dislikes Person" => ClubRelationshipKind.DislikesPerson,
                    _ => ClubRelationshipKind.Other,
                };
                string target = relationship.GetProperty("target").GetString() ?? "";
                string value = relationship.GetProperty("value").GetRawText();
                string shown = $"club.{entry.Name}.{type}.{target}";
                var rows = session.ReadClubRelationships(entry.Address).Where(r => r.Kind == kind).ToList();
                ClubRelationship? match;
                if (kind == ClubRelationshipKind.Rival)
                {
                    match = rows.FirstOrDefault(r =>
                        clubs.FirstOrDefault(c => c.Address == r.TargetClub) is { } club &&
                        ClubNameIs(target, club.Name, club.ShortName));
                }
                else
                {
                    // Match the target object, not its display name: some player rows carry no name at all, and the
                    // manifest says "Mason Greenwood" where the game stores the person at the row's target address.
                    match = rows.FirstOrDefault(r =>
                        NameIs(r.Name, target) ||
                        (r.Target != 0 && players.FirstOrDefault(p => session.PersonOf(p) == r.Target) is { } person && NameIs(person.DisplayName, target)) ||
                        (r.Target != 0 && staffAll.FirstOrDefault(s => s.Address == r.Target) is { } member && NameIs(member.DisplayName, target)));
                }
                Report(shown, value, match is null ? "" : Text(match.Value));
                if (match is null)
                    Console.WriteLine($"      rows: {string.Join(" | ", rows.Select(r => $"{r.Name}={r.Value}").Take(12))}");
            }

        if (root.TryGetProperty("clauses", out var clauses))
        {
            var byName = new Dictionary<string, int>();
            foreach (var p in players)
                if (p.Id is { } id2 && !string.IsNullOrWhiteSpace(p.DisplayName)) byName.TryAdd(FoldName(p.DisplayName), id2);
            var all = session.ReadAllTransferClauses();
            foreach (var clause in clauses.EnumerateArray())
            {
                string playerName = clause.GetProperty("player").GetString() ?? "";
                string type = clause.GetProperty("type").GetString() ?? "";
                string beneficiary = clause.GetProperty("beneficiary").GetString() ?? "";
                var kind = type switch
                {
                    "SellOn" => TransferClauseKind.SellOn,
                    "AppearanceFee" => TransferClauseKind.AppearanceFee,
                    "CompetitionBonus" => TransferClauseKind.CompetitionBonus,
                    _ => (TransferClauseKind?)null,
                };
                string shown = $"clause.{playerName}.{type}.{beneficiary}";
                if (kind is not { } k) { Report(shown, "", "", $"unknown clause type '{type}'"); continue; }
                if (!byName.TryGetValue(FoldName(playerName), out int playerId)) { Report(shown, playerName, "(player name not found)"); continue; }
                var rows = all.Where(c => c.PlayerId == playerId && c.Kind == k &&
                    ClubNameIs(beneficiary, c.Beneficiary?.Name, c.Beneficiary?.ShortName)).ToList();
                if (rows.Count == 0)
                {
                    Report(shown, beneficiary, "(no such clause)");
                    Console.WriteLine($"      candidates: {string.Join(" | ", all.Where(c => c.PlayerId == playerId && c.Kind == k).Select(c => $"{c.Beneficiary?.Name}").Distinct().Take(8))}");
                    continue;
                }
                var chosen = rows[0];
                if (clause.TryGetProperty("total", out var total))
                {
                    int wanted = total.GetInt32();
                    chosen = rows.FirstOrDefault(c => c.Appearances == wanted) ?? chosen;
                }
                if (clause.TryGetProperty("count", out var count))
                {
                    int wanted = count.GetInt32();
                    if (rows.Count == wanted) Report(shown + ".count", count.GetRawText(), Text(rows.Count));
                    else Warn(shown + ".count", count.GetRawText(), Text(rows.Count),
                        "the game stores one row per year/stage; FMRTE may group them in the screenshot");
                }
                if (clause.TryGetProperty("fee", out var fee))
                    Report(shown + ".fee", fee.GetRawText(), Text(chosen.Amount));
                if (clause.TryGetProperty("total", out var total2))
                    Report(shown + ".total", total2.GetRawText(), Text(chosen.Appearances));
                if (clause.TryGetProperty("soFar", out var soFar))
                    Report(shown + ".soFar", soFar.GetRawText(), Text(chosen.AppearancesSoFar),
                        chosen.AppearancesSoFar is null ? "the editor reports appearances-so-far as unknown on this build" : null);
                if (!clause.TryGetProperty("count", out _) && !clause.TryGetProperty("fee", out _) &&
                    !clause.TryGetProperty("total", out _) && !clause.TryGetProperty("soFar", out _))
                    Report(shown, "1", Text(rows.Count));
            }
        }

        // Named anchors: { "kind": "player|club|nation", "name": "...", "field": "...", "value": ... }, values verbatim
        // from FMRTE. Field names are the keys NamedFields prints (`calibrate dump <kind> <name>` lists them).
        if (root.TryGetProperty("named", out var named))
        {
            var cache = new Dictionary<(string, string), (IReadOnlyDictionary<string, string>? Fields, string? Error)>();
            foreach (var item in named.EnumerateArray())
            {
                string kind = item.GetProperty("kind").GetString() ?? "";
                string name = item.GetProperty("name").GetString() ?? "";
                string field = item.GetProperty("field").GetString() ?? "";
                var expected = item.GetProperty("value");
                string expectedText = expected.ValueKind == JsonValueKind.String ? expected.GetString() ?? "" : expected.GetRawText();
                if (!cache.TryGetValue((kind, name), out var entry))
                    cache[(kind, name)] = entry = NamedFields(session, kind, name, players, staffAll, clubs, nations);
                string shown = $"{kind}.{name}.{field}";
                if (entry.Fields is null) { Report(shown, expectedText, "", entry.Error); continue; }
                if (!entry.Fields.TryGetValue(field, out var actual))
                {
                    // A missing indexed row (Unhappiness[1] when the player has one) is a real mismatch, not a skip.
                    if (field.Contains('[')) Report(shown, expectedText, "(no such row)");
                    else Report(shown, expectedText, "", $"no reader for '{field}'");
                    continue;
                }
                Report(shown, expectedText, actual);
            }
        }

        Console.WriteLine($"calibration verify: {pass} passed, {fail} failed, {warn} warnings, {skip} skipped");
        return fail == 0 ? 0 : 1;
    }

    /// <summary>
    /// `calibrate unhappiness <byteOffset>`: for every player with an open motive, the value of one record byte, as a
    /// histogram plus a few names per value, so the flag FMRTE filters on can be checked against its Contract tab.
    /// </summary>
    static int UnhappinessSurvey(string offsetText)
    {
        int at = Convert.ToInt32(offsetText, 16);
        var profiles = GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets"));
        using var session = GameSession.Attach(profiles, false);
        var reader = new UnhappinessReader(session.Memory, session.Profile, session.ModuleBase);
        var today = session.ReadGameDate() ?? DateOnly.MaxValue;
        var groups = new SortedDictionary<int, List<string>>();
        foreach (var p in session.Players.LoadAll())
        {
            IReadOnlyList<PlayerUnhappiness> rows; IReadOnlyList<byte[]> raw;
            try { rows = session.ReadUnhappiness(p); raw = reader.ReadRaw(p); } catch { continue; }
            for (int i = 0; i < rows.Count && i < raw.Count; i++)
            {
                if (rows[i].End is { } e && e <= today) continue;
                if (!groups.TryGetValue(raw[i][at], out var list)) groups[raw[i][at]] = list = [];
                list.Add($"{p.DisplayName} ({p.ClubName}) motive {rows[i].Motive}");
            }
        }
        foreach (var (value, list) in groups)
            Console.WriteLine($"0x{value:X2}: {list.Count}  e.g. {string.Join(" | ", list.Take(6))}");
        return 0;
    }

    /// <summary>`calibrate clausetypes`: appearance-fee clause records grouped by the byte at +0x20, with examples.</summary>
    static int ClauseTypeSurvey()
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets"));
        using var session = GameSession.Attach(profiles, false);
        var players = session.Players.LoadAll().Where(x => x.Id is not null).GroupBy(x => x.Id!.Value).ToDictionary(g => g.Key, g => g.First());
        var groups = new SortedDictionary<string, List<string>>();
        foreach (var c in session.ReadAllTransferClauses().Where(c => c.Kind == TransferClauseKind.AppearanceFee && c.Snapshot is { Length: >= 0x50 }))
        {
            var b = Convert.FromHexString(c.Snapshot!);
            string key = $"+0x20={b[0x20]:X2} +0x21={b[0x21]:X2} +0x22={b[0x22]:X2}";
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add($"{(players.TryGetValue(c.PlayerId, out var p) ? p.DisplayName : c.PlayerId.ToString())} ({c.Beneficiary?.Name}) {c.AppearancesSoFar}/{c.Appearances} fee {c.Amount}");
        }
        foreach (var (key, list) in groups) Console.WriteLine($"{key}: {list.Count}  e.g. {string.Join(" | ", list.Take(5))}");
        return 0;
    }

    /// <summary>`calibrate staffraw "Name" Attribute raw`: puts one staff attribute byte back exactly (after a calibration write).</summary>
    static int StaffRaw(string name, StaffAttribute attribute, byte raw)
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets"));
        using var session = GameSession.Attach(profiles, true);
        var st = session.Staff.LoadAll().Single(x => Fold(x.DisplayName) == Fold(name));
        byte before = session.StaffWriter.ReadAttributeRaw(st, attribute);
        session.StaffWriter.SetAttributeRaw(st, attribute, before, raw);
        Console.WriteLine($"{st.DisplayName} {attribute}: raw {before} -> {raw}");
        return 0;
    }

    /// <summary>`calibrate staffraw "Name"`: prints every staff attribute byte as stored, one "Attribute raw" per line.</summary>
    static int StaffRawAll(string name)
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets"));
        using var session = GameSession.Attach(profiles, true);
        var st = session.Staff.LoadAll().Single(x => Fold(x.DisplayName) == Fold(name));
        foreach (var a in Enum.GetValues<StaffAttribute>()) Console.WriteLine($"{a} {session.StaffWriter.ReadAttributeRaw(st, a)}");
        return 0;
    }

    static string Fold(string? value) => new string((value ?? "").Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Replace('ı', 'i').Replace('ø', 'o').Replace('ł', 'l').Trim();

    static string Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>`calibrate dump player|club|nation "Name"`: every named field the verifier can check, as it reads now.</summary>
    static int Dump(string kind, string name)
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(AppContext.BaseDirectory, "offsets"));
        using var session = GameSession.Attach(profiles, false);
        var (fields, error) = NamedFields(session, kind, name, session.Players.LoadAll(), session.Staff.LoadAll(),
            session.ScanAllClubs(), session.Nations.LoadAll());
        if (fields is null) { Console.WriteLine(error); return 1; }
        foreach (var (key, value) in fields) Console.WriteLine($"{key} = {value}");
        return 0;
    }

    /// <summary>
    /// The values FMRTE shows for one named entity, computed from the editor's readers and turned into FMRTE's text
    /// (labels from <see cref="Fm27Editor.Core.Calibration.FmrteLabels"/>, dates yyyy-MM-dd, money as plain numbers).
    /// </summary>
    static (IReadOnlyDictionary<string, string>? Fields, string? Error) NamedFields(GameSession session, string kind, string name,
        IReadOnlyList<PlayerRecord> players, IReadOnlyList<StaffRecord> staff, IReadOnlyList<ClubEntry> clubs, IReadOnlyList<NationRecord> nations)
    {
        var f = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string I(object? v) => v switch { null => "", IFormattable x => x.ToString(null, CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
        string want = Fold(name);
        switch (kind)
        {
            case "player":
            {
                var matches = players.Where(p => Fold(p.DisplayName) == want).ToList();
                if (matches.Count != 1) return (null, $"{matches.Count} players named '{name}'");
                var p = matches[0];
                f["Club"] = p.Club?.DisplayName ?? "";
                f["CurrentAbility"] = I(p.CurrentAbility);
                f["PotentialAbility"] = I(p.PotentialAbility);
                var c = session.Contracts.Read(p);
                if (c is not null)
                {
                    f["TransferStatus"] = Fm27Editor.Core.Calibration.FmrteLabels.Of(c.Transfer);
                    f["LoanStatus"] = Fm27Editor.Core.Calibration.FmrteLabels.Of(c.Loan);
                    if (c.OnRollingContract is { } r) f["OnRollingContract"] = r ? "1" : "0";
                    if (c.PlayingTime is not null) f["AgreedPlayingTime"] = Fm27Editor.Core.Calibration.FmrteLabels.Of(c.PlayingTime);
                    if (c.PreviousPlayingTime is not null) f["PreviousPlayingTime"] = Fm27Editor.Core.Calibration.FmrteLabels.Of(c.PreviousPlayingTime);
                    if (c.FuturePlayingTime is not null) f["FuturePlayingTime"] = Fm27Editor.Core.Calibration.FmrteLabels.Of(c.FuturePlayingTime);
                    f["Wage"] = I(c.Wage);
                    f["Expires"] = Iso(c.Expires);
                    if (c.Type is { } ct) f["ContractType"] = ct.ToString();
                    if (c.SquadNumber is { } sn) f["SquadNumber"] = I(sn);
                    if (c.LoyaltyBonus is { } lb) f["LoyaltyBonus"] = I(lb);
                }
                // Values as FMRTE's player pages show them (1-20 attributes, positions, hidden personality).
                f["Born"] = Iso(p.DateOfBirth);
                f["Height"] = I(p.Height);
                f["HomeReputation"] = I(p.HomeReputation);
                f["CurrentReputation"] = I(p.CurrentReputation);
                f["WorldReputation"] = I(p.WorldReputation);
                f["Nationality"] = p.Nationality?.Name ?? "";
                foreach (var a in Enum.GetValues<PlayerAttribute>()) f[$"Attribute.{a}"] = I(p.Attribute(a));
                foreach (var pos in Enum.GetValues<PlayerPosition>()) f[$"Position.{pos}"] = I(p.Position(pos));
                foreach (var pa in Enum.GetValues<PersonalityAttribute>()) f[$"Personality.{pa}"] = I(p.PersonalityValue(pa));
                foreach (var (field, value) in session.PlayerFields.ReadAll(p)) f[$"Field.{field}"] = I(value);
                var today = session.ReadGameDate() ?? DateOnly.MaxValue;
                var raw = new UnhappinessReader(session.Memory, session.Profile, session.ModuleBase).ReadRaw(p);
                var all = session.ReadUnhappiness(p);
                string Who(int id) => id <= 0 ? "-" : players.FirstOrDefault(x => x.PersonIndex == id)?.DisplayName
                    ?? staff.FirstOrDefault(x => x.Id == id)?.DisplayName ?? $"id {id}";
                for (int i = 0; i < raw.Count; i++)
                {
                    f[$"raw.Unhappiness[{i}]"] = Convert.ToHexString(raw[i]);
                    if (i < all.Count)
                        f[$"raw.Unhappiness[{i}].decoded"] = $"start {Iso(all[i].Start)} end {Iso(all[i].End)} item1 {Who(all[i].Item1)} item2 {Who(all[i].Item2)} motive {all[i].Motive} b18..1B {raw[i][0x18]:X2} {raw[i][0x19]:X2} {raw[i][0x1A]:X2} {raw[i][0x1B]:X2} b10 {BitConverter.ToInt32(raw[i], 0x10)} b14 {BitConverter.ToInt16(raw[i], 0x14)}";
                }
                var rows = session.ReadUnhappiness(p).Where(u => u.IsActive(today)).ToList();
                f["Unhappiness.Count"] = I(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    var u = rows[i];
                    f[$"Unhappiness[{i}].Motive"] = I(u.Motive);
                    f[$"Unhappiness[{i}].Type"] = Fm27Editor.Core.Calibration.FmrteLabels.UnhappinessTypeOf(u.Type);
                    f[$"Unhappiness[{i}].Start"] = Iso(u.Start);
                    f[$"Unhappiness[{i}].End"] = u.End is { } e ? Iso(e) : "1900-01-01";
                    string person = u.Item1 <= 0 ? "" : players.FirstOrDefault(x => x.PersonIndex == u.Item1)?.DisplayName
                        ?? staff.FirstOrDefault(x => x.Id == u.Item1)?.DisplayName ?? $"id {u.Item1}";
                    f[$"Unhappiness[{i}].Person"] = person;
                }
                if (Environment.GetEnvironmentVariable("DUMP_CLAUSES") == "1")
                {
                    var clauses = session.ReadAllTransferClauses().Where(c => c.PlayerId == p.Id).ToList();
                    for (int i = 0; i < clauses.Count; i++)
                    {
                        var cl = clauses[i];
                        f[$"raw.Clauses[{i}]"] = $"{cl.Kind} cond {cl.Condition} {cl.Beneficiary?.Name} fee {cl.Amount} apps {cl.AppearancesSoFar}/{cl.Appearances} comp {cl.Competition?.Name} ends {Iso(cl.Ends)} bytes {cl.Snapshot}";
                    }
                }
                var offers = session.Proposals.Read(p).Where(o => TransferProposalReader.ShownByFmrte(o.Status)).ToList();
                f["Proposals.Count"] = I(offers.Count);
                for (int i = 0; i < offers.Count; i++)
                {
                    var o = offers[i];
                    f[$"Proposals[{i}].Type"] = Fm27Editor.Core.Calibration.FmrteLabels.Of(o.Type);
                    f[$"Proposals[{i}].From"] = o.From ?? "";
                    f[$"Proposals[{i}].To"] = o.To ?? "";
                    f[$"Proposals[{i}].Fee"] = I(o.Value);
                    f[$"Proposals[{i}].Status"] = Fm27Editor.Core.Calibration.FmrteLabels.ProposalStatusOf(o.Status);
                    f[$"Proposals[{i}].StatusCode"] = I(o.Status);
                }
                return (f, null);
            }
            case "club":
            {
                var matches = clubs.Where(c => Fold(c.Name) == want || Fold(c.ShortName) == want).ToList();
                if (matches.Count == 0) return (null, $"no club named '{name}'");
                // Names repeat (the women's side shares the name): prefer the men's club, then the one with most teams.
                var club = matches.OrderByDescending(c => c.Gender == Gender.Men).ThenByDescending(c => c.Teams.Count).First();
                f["Id"] = I(club.Id);
                if (club.FirstTeam is { } first)
                    foreach (var (field, value) in session.ClubFields.ReadAll(club.Address, first.Address)) f[$"Field.{field}"] = I(value);
                f["raw.Address"] = $"0x{club.Address:X}";
                if (club.FirstTeam is { } ft) f["raw.FirstTeam"] = $"0x{ft.Address:X}";
                // Captain / Vice Captain: the first team's slot holds a person block (player + relations.playerPerson).
                if (club.FirstTeam is { } team && session.Profile.Team is { } to && session.Profile.Relations is { } ro)
                {
                    var byPerson = players.GroupBy(p => p.Address + (ulong)ro.PlayerPerson).ToDictionary(g => g.Key, g => g.First());
                    foreach (var (key, offset) in new[] { ("Captain", to.Captain), ("ViceCaptain", to.ViceCaptain) })
                        if (offset is { } off)
                        {
                            ulong person = session.Memory.ReadUInt64(team.Address + (ulong)off);
                            f[key] = person == 0 ? "none" : byPerson.TryGetValue(person, out var holder) ? holder.DisplayName : $"(person 0x{person:X})";
                        }
                }
                ulong nation = session.NationOfClub(club.Address);
                f["Nation"] = nations.FirstOrDefault(n => n.Address == nation)?.DisplayName ?? "";
                var names = session.Competitions.LoadAll().Where(c => !string.IsNullOrWhiteSpace(c.Name)).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name!);
                var divisions = session.ReadRegionalDivisions(club.Address);
                f["RegionalDivisions.Count"] = I(divisions.Count);
                for (int i = 0; i < divisions.Count; i++)
                {
                    var d = divisions[i];
                    f[$"RegionalDivisions[{i}].Name"] = d.CompetitionId is { } id && names.TryGetValue(id, out var n) ? n : "";
                    f[$"RegionalDivisions[{i}].Level"] = I(d.Level);
                    f[$"RegionalDivisions[{i}].Year"] = I(d.Year);
                }
                try
                {
                    var youth = session.YouthCandidates.ReadForClub(club.Address);
                    f["Youth.Count"] = I(youth.Count);
                    foreach (var y in youth)
                    {
                        string k = $"Youth[{y.DisplayName}]";
                        f[$"{k}.Born"] = Iso(y.DateOfBirth);
                        f[$"{k}.Height"] = I(y.Height);
                        f[$"{k}.CurrentAbility"] = I(y.CurrentAbility);
                        f[$"{k}.PotentialAbility"] = I(y.PotentialAbility);
                    }
                }
                catch (Exception ex) { f["Youth.Count"] = $"(unreadable: {ex.Message})"; }
                return (f, null);
            }
            case "staff":
            {
                var matches = staff.Where(x => Fold(x.DisplayName) == want).ToList();
                if (matches.Count != 1) return (null, $"{matches.Count} staff named '{name}'");
                var st = matches[0];
                foreach (var c in Enum.GetValues<TrainingCategory>())
                    f[$"TrainingRating.{c}"] = TrainingRatings.Stars(st, c).ToString("0.0", CultureInfo.InvariantCulture);
                foreach (var a in Enum.GetValues<StaffAttribute>()) f[$"Attribute.{a}"] = I(st.Attribute(a));
                if (st.Caps is { } caps) f["Caps"] = I(caps);
                if (st.Goals is { } goals) f["Goals"] = I(goals);
                if (st.License is { } lic) f["License"] = StaffDetailsMassEdit.Licenses.FirstOrDefault(l => l.Value == lic).Name ?? lic.ToString();
                if (st.Roles is { } rs) foreach (var r in Enum.GetValues<StaffRole>()) f[$"Roles.{StaffDetailsMassEdit.RoleLabel(r)}"] = I(rs[(int)r]);
                f["Nationality"] = st.Nationality?.Name ?? "";
                f["Born"] = Iso(st.DateOfBirth);
                f["CurrentAbility"] = I(st.CurrentAbility);
                f["PotentialAbility"] = I(st.PotentialAbility);
                foreach (var t in Enum.GetValues<StaffPersonality>()) f[$"Personality.{t}"] = I(st.Personality[(int)t]);
                if (st.ManagerAttributes is { } mgr) foreach (var m in Enum.GetValues<ManagerAttribute>()) f[$"Manager.{m}"] = I(mgr[(int)m]);
                return (f, null);
            }
            case "staffcount":
            {
                // name = training category; fields "min-max" give how many staff FMRTE's Advanced Search returns for that range.
                if (!Enum.TryParse<TrainingCategory>(name, out var category)) return (null, $"no training category '{name}'");
                foreach (var range in new[] { "3-5", "3.5-5", "4-5", "4.5-5", "0-1" })
                {
                    var parts = range.Split('-');
                    double lo = double.Parse(parts[0], CultureInfo.InvariantCulture), hi = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    f[$"Count.{range}"] = I(staff.Count(x => TrainingRatings.Stars(x, category) is var v && v >= lo && v <= hi));
                }
                return (f, null);
            }
            case "stadium":
            {
                var matches = session.Stadiums.LoadAll().Where(x => Fold(x.DisplayName) == want).ToList();
                if (matches.Count != 1) return (null, $"{matches.Count} stadiums named '{name}'");
                var st = matches[0];
                f["Id"] = I(st.Id);
                f["Owner"] = st.Owner?.Name ?? "";
                f["Latitude"] = st.Latitude.ToString("G7", CultureInfo.InvariantCulture);
                f["Longitude"] = st.Longitude.ToString("G7", CultureInfo.InvariantCulture);
                f["Stands"] = I(st.Stands.Count);
                f["StandsTotal"] = I(st.StandsTotal);
                f["IntLength"] = st.IntLength is { } il ? I(il / 10.0) : "unset";
                f["IntWidth"] = st.IntWidth is { } iw ? I(iw / 10.0) : "unset";
                foreach (var (field, value) in new Fm27Editor.Core.Stadiums.StadiumWriter(session.Memory, session.Profile, session.ModuleBase).ReadFields(st.Address))
                    f[field.ToString()] = I(value);
                return (f, null);
            }
            case "competition":
            {
                var matches = session.Competitions.LoadAll(() => players).Where(x => Fold(x.DisplayName) == want).ToList();
                if (matches.Count != 1) return (null, $"{matches.Count} competitions named '{name}'");
                var c = matches[0];
                f["Id"] = I(c.Id);
                f["ShortName"] = c.ShortName ?? "";
                f["Reputation"] = I(c.Reputation);
                f["Foreground"] = $"#{c.Foreground:X8}";
                f["Background"] = $"#{c.Background:X8}";
                return (f, null);
            }
            case "award":
            {
                var matches = session.Awards.LoadAll().Where(x => Fold(x.DisplayName) == want).ToList();
                if (matches.Count != 1) return (null, $"{matches.Count} awards named '{name}'");
                var a = matches[0];
                f["Id"] = I(a.Id);
                f["Reputation"] = I(a.Reputation);
                f["WinnerHomeReputation"] = I(a.WinnerHomeReputation);
                f["WinnerWorldReputation"] = I(a.WinnerWorldReputation);
                return (f, null);
            }
            case "nation":
            {
                var nation = nations.FirstOrDefault(n => Fold(n.DisplayName) == want);
                if (nation is null) return (null, $"no nation named '{name}'");
                f["Continent"] = session.NationLinks.Continent(nation.Address)?.Name ?? "";
                foreach (var (field, value) in session.NationFields.ReadAll(nation.Address)) f[$"Field.{field}"] = I(value);
                return (f, null);
            }
        }
        return (null, $"unknown kind '{kind}'");
    }
}
