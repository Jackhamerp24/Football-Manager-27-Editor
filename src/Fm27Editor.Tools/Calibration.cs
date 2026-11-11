using System.Globalization;
using System.Text.Json;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

/// <summary>
/// Calibration-save tooling: resolve the entities named by calibration/manifest.json on the attached
/// game and report where they live. Marker scanning and expected-value checks build on this.
/// </summary>
public static class Calibration
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: calibrate entities|verify <manifest.json>"); return 2; }
        return args[0] switch
        {
            "entities" => Entities(args[1]),
            "verify" => Verify(args[1]),
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

        static bool Same(string expected, string actual) =>
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
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

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
                if (!byName.TryGetValue(FoldName(playerName), out int playerId)) { Report(shown, "", ""); Console.WriteLine("      player name not found"); continue; }
                var rows = all.Where(c => c.PlayerId == playerId && c.Kind == k &&
                    ClubNameIs(beneficiary, c.Beneficiary?.Name, c.Beneficiary?.ShortName)).ToList();
                if (rows.Count == 0)
                {
                    Report(shown, "", "");
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

        Console.WriteLine($"calibration verify: {pass} passed, {fail} failed, {warn} warnings, {skip} skipped");
        return fail == 0 ? 0 : 1;
    }
}
