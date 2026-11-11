using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

/// <summary>
/// steamwrites: one guarded write per family on the loaded game, undone straight after (a value set and put back, a row added
/// and removed), each checked by reading the game again. It changes the open save for a moment, so use a junk save.
/// </summary>
static class SteamWrites
{
    static int failed;

    static void Step(string name, Action action)
    {
        try { action(); Console.WriteLine($"ok    {name}"); }
        catch (Exception ex) { failed++; Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
    }

    static void Expect(bool condition, string what) { if (!condition) throw new InvalidOperationException("read back differs: " + what); }

    public static int Run(GameSession s)
    {
        var players = s.Players.LoadAll();
        var player = players.First(p => p.Id == 2000123586);                       // Kobbie Mainoo
        var clubs = s.LoadClubs(players);
        var club = clubs.First(c => c.Id == 1736);                                 // Real Madrid
        var nations = s.Nations.LoadAll();
        var england = nations.First(n => n.DisplayName == "England");

        Step("language add then remove", () =>
        {
            var current = s.ReadLanguages(player);
            var entry = s.LanguageCatalogue(players).First(l => current.All(c => c.LanguageId != l.Id));
            s.AddLanguage(player, current, entry, 9);
            var after = s.ReadLanguages(player);
            var added = after.FirstOrDefault(l => l.LanguageId == entry.Id);
            Expect(added is not null && added.Value == 9, "language row");
            s.RemoveLanguage(player, after, added!);
            Expect(s.ReadLanguages(player).All(l => l.LanguageId != entry.Id), "language removed");
        });

        Step("work permit add then remove", () =>
        {
            s.AddWorkPermit(player, england.Address, 2031);
            var grant = s.ReadWorkPermits(player).FirstOrDefault(g => g.Nation == england.Address);
            Expect(grant is not null, "permit row");
            s.RemoveWorkPermit(player, grant!);
            Expect(s.ReadWorkPermits(player).All(g => g.Nation != england.Address), "permit removed");
        });

        Step("promise add then remove", () =>
        {
            var store = s.Promises;
            var type = PromiseTypes.All.First(t => t.Parameter == PromiseParameter.None);
            var date = s.ReadGameDate() ?? throw new InvalidOperationException("no game date");
            // Mainoo has no promise on every save; copy from the first player who has one.
            var (owner, existing) = players
                .Select(p => (Owner: s.Owner(p), Rows: store.Read(s.Owner(p))))
                .FirstOrDefault(x => x.Rows.Count > 0);
            if (existing is null) throw new InvalidOperationException("no player with a promise on this save");
            ulong manager = existing.FirstOrDefault()?.Manager ?? throw new InvalidOperationException("the player has no promise to copy the manager from");
            ulong clubPointer = existing.First().Club;
            var added = store.Add(owner, manager, clubPointer, new PromiseValues(type.Id, date, date.AddDays(60), -1, -1), size => s.AllocateGameBlock(size));
            Expect(store.Read(owner).Any(p => p.Address == added.Address && p.TypeId == type.Id), "promise row");
            store.Remove(owner, store.Read(owner).First(p => p.Address == added.Address));
            Expect(store.Read(owner).All(p => p.Address != added.Address), "promise removed");
        });

        Step("club field set then restore", () =>
        {
            var fields = s.ClubFields;
            long before = fields.Read(club.Address, ClubField.Morale) ?? throw new InvalidOperationException("no morale");
            long other = before == 14 ? 13 : 14;
            fields.Set(club.Address, ClubField.Morale, before, other);
            Expect(fields.Read(club.Address, ClubField.Morale) == other, "morale");
            fields.Set(club.Address, ClubField.Morale, other, before);
            Expect(fields.Read(club.Address, ClubField.Morale) == before, "morale restored");
        });

        Step("nation field set then restore", () =>
        {
            var fields = s.NationFields;
            long before = fields.Read(england.Address, NationField.EconomicFactor) ?? throw new InvalidOperationException("no economic factor");
            long other = before == 18 ? 17 : 18;
            fields.Set(england.Address, NationField.EconomicFactor, before, other);
            Expect(fields.Read(england.Address, NationField.EconomicFactor) == other, "economic factor");
            fields.Set(england.Address, NationField.EconomicFactor, other, before);
            Expect(fields.Read(england.Address, NationField.EconomicFactor) == before, "economic factor restored");
        });

        Step("club vision importance set then restore", () =>
        {
            var team = club.FirstTeam ?? throw new InvalidOperationException("no first team");
            var rows = s.ReadClubVision(team.Address, null);
            var row = rows[0];
            int before = row.Importance, other = before == 7 ? 6 : 7;
            s.SetClubVisionImportance(team.Address, row, other);
            Expect(s.ReadClubVision(team.Address, null)[0].Importance == other, "importance");
            s.SetClubVisionImportance(team.Address, s.ReadClubVision(team.Address, null)[0], before);
            Expect(s.ReadClubVision(team.Address, null)[0].Importance == before, "importance restored");
        });

        Step("stadium field set then restore", () =>
        {
            // Any named stadium: the save may not contain a particular club's ground (the old fixed name failed on Game Pass).
            var all = s.Stadiums.LoadAll();
            var stadium = all.FirstOrDefault(x => x.DisplayName == "Old Trafford")
                ?? all.First(x => !string.IsNullOrEmpty(x.DisplayName));
            var writer = s.StadiumWriter;
            var capacity = Fm27Editor.Core.Stadiums.StadiumField.Capacity;
            long before = writer.ReadFields(stadium.Address)[capacity];
            writer.SetField(stadium.Address, capacity, before, before + 1);
            Expect(writer.ReadFields(stadium.Address)[capacity] == before + 1, "capacity");
            writer.SetField(stadium.Address, capacity, before + 1, before);
            Expect(writer.ReadFields(stadium.Address)[capacity] == before, "capacity restored");
        });

        Step("club balance set then restore", () =>
        {
            var fields = s.ClubFields;
            long before = fields.Read(club.Address, ClubField.Balance) ?? throw new InvalidOperationException("no balance");
            fields.Set(club.Address, ClubField.Balance, before, before + 1);
            Expect(fields.Read(club.Address, ClubField.Balance) == before + 1, "balance");
            fields.Set(club.Address, ClubField.Balance, before + 1, before);
            Expect(fields.Read(club.Address, ClubField.Balance) == before, "balance restored");
        });

        Step("transfer clause set then restore", () =>
        {
            var writer = s.TransferClauses;
            Fm27Editor.Core.Clubs.TransferClause? Find(Func<Fm27Editor.Core.Clubs.TransferClause, bool> pick) =>
                clubs.Select(c => s.ReadTransferClauses(c.Address)).SelectMany(x => x).FirstOrDefault(pick);
            var c = Find(x => x.Kind == TransferClauseKind.SellOn && x.Percentage is > 1 and < 100)
                ?? Find(x => x.Kind != TransferClauseKind.SellOn && x.Amount is > 0)
                ?? throw new InvalidOperationException("no clause on this save");
            var club2 = clubs.First(x => s.ReadTransferClauses(x.Address).Any(y => y.Address == c.Address));
            Fm27Editor.Core.Clubs.TransferClause Again() => s.ReadTransferClauses(club2.Address).First(y => y.Address == c.Address);
            if (c.Kind == TransferClauseKind.SellOn)
            {
                int before = c.Percentage!.Value;
                writer.SetPercentage(c, before + 1);
                Expect(Again().Percentage == before + 1, "percentage");
                writer.SetPercentage(Again(), before);
                Expect(Again().Percentage == before, "percentage restored");
            }
            else
            {
                int before = c.Amount!.Value;
                writer.SetAmount(c, before + 1);
                Expect(Again().Amount == before + 1, "amount");
                writer.SetAmount(Again(), before);
                Expect(Again().Amount == before, "amount restored");
            }
        });

        Step("loan wage set then restore", () =>
        {
            var loan = players.Select(p => s.ReadLoan(p)).FirstOrDefault(l => l is not null && l.Terms.Wage > 0)
                ?? throw new InvalidOperationException("no loan with a wage on this save");
            int before = loan.Terms.Wage;
            s.SetLoanTerms(loan, new(Wage: before + 1));
            var after = s.ReadLoan(loan.Player)!;
            Expect(after.Terms.Wage == before + 1, "loan wage");
            s.SetLoanTerms(after, new(Wage: before));
            Expect(s.ReadLoan(loan.Player)!.Terms.Wage == before, "loan wage restored");
        });

        foreach (var type in new[] { ProposalType.Transfer, ProposalType.Loan, ProposalType.Trial, ProposalType.BClubMove })
            Step($"{type} offer value set then restore", () =>
            {
                var hit = players.Select(p => (p, s.Proposals.Read(p).FirstOrDefault(x => x.Type == type && x.Value >= 0)))
                    .FirstOrDefault(x => x.Item2 is not null);
                if (hit.Item2 is null) throw new InvalidOperationException($"no {type} offer on this save");
                var (owner, offer) = (hit.p, hit.Item2);
                TransferProposal Again() => s.Proposals.Read(owner).First(x => x.Address == offer.Address);
                s.ProposalWriter.SetValue(offer, offer.Value + 1);
                Expect(Again().Value == offer.Value + 1, "offer value");
                s.ProposalWriter.SetValue(Again(), offer.Value);
                Expect(Again().Value == offer.Value, "offer value restored");
            });

        Step("preset heal", () => s.Heal(player));
        Step("preset inspire", () => s.Inspire(player));
        Step("preset team cohesion", () => s.SetTeamCohesion(player, 100));
        Step("preset register for UEFA", () => { s.RegisterForUefa(player); });
        Step("preset register for domestic", () => { s.RegisterForDomestic(player); });
        Step("preset trained at club", () => { s.TrainedAtCurrentClub(player); });
        Step("preset trained in nation", () => { s.TrainedInCurrentNation(player); });
        Step("preset remove int. withdrawal", () => { s.RemoveInternationalWithdrawal(player); });
        Step("preset tactical understanding", () => s.TacticalUnderstanding(player));
        Step("squad preset: players without registration", () => { _ = s.PlayersWithoutRegistration(club.FirstTeam!.Address, RegistrationKind.Uefa); });

        Console.WriteLine(failed == 0 ? "all steps passed" : $"{failed} step(s) failed");
        return failed == 0 ? 0 : 1;
    }
}
