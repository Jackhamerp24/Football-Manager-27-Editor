using System.Collections;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

/// <summary>
/// steamcheck: calls every reader the app uses on a sample of the game's objects and reports, per feature, how many calls
/// returned something and how many threw. A feature that returns nothing everywhere, or throws, has no usable profile entry
/// on this build. Read only.
/// </summary>
static class SteamCheck
{
    static int Count(object? value)
    {
        if (value is null) return 0;
        if (value is string) return 1;
        if (value is IEnumerable e) { int n = 0; foreach (var _ in e) n++; return n; }
        int total = 0;
        foreach (var p in value.GetType().GetProperties())
            if (p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.GetIndexParameters().Length == 0)
                try { if (p.GetValue(value) is IEnumerable inner) foreach (var _ in inner) total++; } catch { }
        return total > 0 ? total : 1;
    }

    /// <summary>Only the features whose name contains this text (steamcheck &lt;text&gt;); empty runs all.</summary>
    public static string Only = "";
    static readonly List<string> Report = [];
    static int failures;

    static void Feature<T>(string name, IEnumerable<T> sample, Func<T, object?> read)
    {
        if (Only.Length > 0 && !name.Contains(Only, StringComparison.OrdinalIgnoreCase)) return;
        int tried = 0, filled = 0, threw = 0; string? error = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var item in sample)
        {
            tried++;
            try { if (Count(read(item)) > 0) filled++; }
            catch (Exception ex) { threw++; error ??= ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
        }
        string verdict = threw > 0 && filled == 0 ? "FAIL " : filled == 0 ? "EMPTY" : "ok   ";
        if (verdict != "ok   ") failures++;
        string line = $"{verdict} {name,-36} filled {filled}/{tried} ({watch.ElapsedMilliseconds} ms)" + (threw > 0 ? $", threw {threw} ({error})" : "");
        Report.Add(line); Console.WriteLine(line); Console.Out.Flush();
    }

    static IEnumerable<T> Spread<T>(IReadOnlyList<T> all, int max) =>
        all.Count <= max ? all : Enumerable.Range(0, max).Select(i => all[(int)((long)i * all.Count / max)]);

    public static int Run(GameSession s)
    {
        var allPlayers = s.Players.LoadAll();
        var players = Spread(allPlayers, 3000).ToList();
        var staff = Spread(s.Staff.LoadAll(), 1500).ToList();
        var clubs = Spread(s.LoadClubs(allPlayers), 500).ToList();
        var nationsAll = s.Nations.LoadAll();
        var nations = Spread(nationsAll, 60).ToList();
        var comps = Spread(s.Competitions.LoadAll(), 300).ToList();
        var continents = s.NationLinks.Continents(nationsAll);

        Feature("player statuses (relations)", players, p => s.ReadStatuses(p));
        Feature("player bans", players, p => s.ReadBans(p));
        Feature("player languages", players, p => s.ReadLanguages(p));
        Feature("player unhappiness", players, p => s.ReadUnhappiness(p));
        Feature("player work permit grants", players, p => s.ReadWorkPermits(p));
        Feature("player work permit denied", allPlayers, p => s.ReadWorkPermitDenied(p));
        Feature("player injuries", players, p => s.ReadInjuries(p));
        Feature("player career plans", players, p => s.CareerPlans.Read(p));
        Feature("player proposals", players, p => s.Proposals.Read(p));
        Feature("player loan offers", allPlayers, p => s.Proposals.Read(p).Where(x => x.Type == ProposalType.Loan).ToList());
        Feature("player trial offers", allPlayers, p => s.Proposals.Read(p).Where(x => x.Type == ProposalType.Trial).ToList());
        Feature("player B club move offers", allPlayers, p => s.Proposals.Read(p).Where(x => x.Type == ProposalType.BClubMove).ToList());
        Feature("player gender", players, p => p.Gender == Fm27Editor.Core.Offsets.Gender.Unknown ? null : p.Gender);
        Feature("player contract", players, p => s.Contracts.Read(p));
        Feature("player clauses", players, p => s.ReadClauses(p));
        Feature("player current loan", players, p => s.ReadCurrentLoan(p));
        Feature("player promises", players, p => s.Promises.Read(s.Owner(p)));
        Feature("player tactical understanding", players, p => s.PlayerTactics.Read(p));
        Feature("player national call-ups", new object[] { 0 }, _ => s.CallUps.All());

        Feature("staff knowledge", staff, x => s.StaffKnowledge.Read(x, nationsAll));
        Feature("staff tendencies", staff, x => s.StaffTendencies.Read(x));
        Feature("staff formations", staff, x => s.StaffFormations.Read(x));
        Feature("staff contract", staff, x => s.StaffContracts.Read(x));
        Feature("staff career plans", staff, x => s.CareerPlans.Read(s.Owner(x)));
        Feature("staff languages", staff, x => s.ReadLanguages(s.Owner(x)));
        Feature("staff statuses (relations)", staff, x => s.ReadStatuses(s.Owner(x)));

        Feature("club finance", clubs, c => s.ReadFinance(c.Address));
        Feature("club fields", clubs, c => c.FirstTeam is { } t ? s.ClubFields.ReadAll(c.Address, t.Address) : null);
        Feature("club gender", clubs, c => c.Gender == Fm27Editor.Core.Offsets.Gender.Unknown ? null : c.Gender);
        Feature("club affiliations", clubs, c => s.ReadAffiliations(c.Address));
        Feature("club regional divisions", clubs, c => s.ReadRegionalDivisions(c.Address));
        Feature("club sponsors", clubs, c => s.ReadSponsors(c.Address));
        Feature("club debts", clubs, c => s.ReadDebts(c.Address));
        Feature("club relationships", clubs, c => s.ReadClubRelationships(c.Address));
        Feature("club transfer clauses", clubs, c => s.ReadTransferClauses(c.Address));
        Feature("club vision", clubs, c => c.FirstTeam is { } t ? s.ReadClubVision(t.Address, null) : null);
        Feature("club kits", clubs, c => s.ClubKits.Read(c.Address, c.Id ?? 0));
        Feature("club financial statements", clubs, c => s.FinancialStatements.Read(c.Address));
        Feature("club squad", clubs, c => c.FirstTeam is { } t ? s.ReadSquad(t.Address) : null);

        Feature("nation fields", nations, n => s.NationFields.ReadAll(n.Address));
        Feature("nation teams", nations, n => s.NationTeams.Read(n.Address));
        Feature("nation kits", nations, n => s.NationKits.Read(n.Address));
        Feature("nation colours", nations, n => s.NationColours.Read(n.Address));
        Feature("nation euro coefficients", nations, n => s.NationEuroCoefficients.Read(n.Address));
        Feature("nation rivalries", nations, n => s.NationRivalries.Read(n, nationsAll));
        Feature("nation preferences", nations, n => s.NationPreferences.Read(n, nationsAll));
        Feature("nation staff", nations, n => s.NationStaff.Read(n.Address));
        Feature("nation transfer values", nations, n => s.ReadNationTransferValues(n));
        Feature("nation squads", nations, n => s.NationSquads.ReadNation(n.Address));

        Feature("competition prize money", comps, c => s.CompetitionPrizeMoney.Read(c));
        Feature("competition standings", comps, c => s.CompetitionStandings.Read(c));
        Feature("competition VAR", comps, c => s.CompetitionVar.Read(c));
        Feature("competition squad rules", comps, c => s.CompetitionSquadRules.Read(c));

        Feature("stadiums", new object[] { 0 }, _ => s.Stadiums.LoadAll());
        Feature("cities", new object[] { 0 }, _ => s.Cities.LoadAll());
        Feature("awards", new object[] { 0 }, _ => s.Awards.LoadAll());
        Feature("referees", new object[] { 0 }, _ => s.ReadReferees());
        Feature("injury types", new object[] { 0 }, _ => s.InjuryTypes());
        Feature("continents", new object[] { 0 }, _ => continents);
        Feature("planned retirements", new object[] { 0 }, _ => new PlannedRetirements(s.Memory, s.Profile, s.ModuleBase).ReadAll());
        Feature("nation agreement regions", new object[] { 0 }, _ => s.NationAgreements.Regions());
        Feature("transfer deals (heap scan)", new object[] { 0 }, _ => s.TransferDeals.Scan());

        void Named<T>(string what, IReadOnlyList<T> all, Func<T, string?> name)
        {
            int named = all.Count(x => !string.IsNullOrWhiteSpace(name(x)));
            bool bad = all.Count > 0 && named < all.Count * 0.9;
            if (bad) failures++;
            string named_ = $"{(bad ? "NAMES" : "ok   ")} {what + " with a name",-36} {named}/{all.Count}"; Report.Add(named_); Console.WriteLine(named_); Console.Out.Flush();
        }
        Named("players", allPlayers, p => p.DisplayName);
        Named("staff", s.Staff.LoadAll(), x => x.DisplayName);
        Named("clubs", s.LoadClubs(allPlayers), c => c.Name);
        Named("nations", nationsAll, n => n.DisplayName);
        Named("competitions", s.Competitions.LoadAll(), c => c.DisplayName);
        Named("stadiums", s.Stadiums.LoadAll(), x => x.Name);
        Named("cities", s.Cities.LoadAll(), c => c.Name);
        Named("awards", s.Awards.LoadAll(), x => x.DisplayName);

        // Rows that point at a player must resolve that player's person object (the 27.0.1 person vtable was
        // missing, so every player target in a club's relationships showed a blank name).
        var relationshipRows = clubs.SelectMany(c => s.ReadClubRelationships(c.Address))
            .Where(r => r.Kind is Fm27Editor.Core.Clubs.ClubRelationshipKind.Rival
                or Fm27Editor.Core.Clubs.ClubRelationshipKind.LikesPerson
                or Fm27Editor.Core.Clubs.ClubRelationshipKind.DislikesPerson).ToList();
        bool KnownPersonTarget(ulong target)
        {
            if (target == 0 || s.Profile.Relations is not { } rel) return false;
            Span<byte> bytes = stackalloc byte[8];
            if (!s.Memory.TryRead(target, bytes)) return false;
            ulong vt = BitConverter.ToUInt64(bytes) - s.ModuleBase;
            return vt == rel.PersonVtable || vt == rel.StaffPersonVtable ||
                (s.Profile.Staff?.Kinds.Any(k => vt == k.PersonVtableRva) ?? false);
        }
        var knownRows = relationshipRows.Where(r => !string.IsNullOrWhiteSpace(r.Name) || KnownPersonTarget(r.Target)).ToList();
        int blankNames = knownRows.Count(r => string.IsNullOrWhiteSpace(r.Name));
        int unknownTargets = relationshipRows.Count(r => string.IsNullOrWhiteSpace(r.Name) && !KnownPersonTarget(r.Target));
        if (blankNames > 0) failures++;
        string relationshipLine = $"{(blankNames == 0 ? "ok   " : "NAMES")} {"club relationship target names",-36} {knownRows.Count - blankNames}/{knownRows.Count}" +
            (unknownTargets > 0 ? $" ({unknownTargets} retired/official targets have no name reader)" : "");
        Report.Add(relationshipLine); Console.WriteLine(relationshipLine); Console.Out.Flush();

        Console.WriteLine($"{Report.Count} features, {failures} empty or failing");
        return failures == 0 ? 0 : 1;
    }
}
