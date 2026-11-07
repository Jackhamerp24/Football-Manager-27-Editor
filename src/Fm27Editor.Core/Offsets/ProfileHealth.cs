using Fm27Editor.Core.Awards;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Stadiums;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Offsets;

/// <summary>One module's verdict after reading real objects with a candidate profile. A check must fail when the
/// profile it is given has a wrong offset in that module, otherwise it proves nothing.</summary>
public sealed record ModuleHealth(string Module, bool Ok, string Detail);

/// <summary>
/// Reads a sample of every module with a candidate profile and reports PASS/FAIL. The checks look at values that a
/// shifted offset cannot fake: ids, abilities, printable names, vtables of linked objects, plausible dates.
/// </summary>
public sealed class ProfileHealth(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public IReadOnlyList<ModuleHealth> Run() =>
    [
        Guard("players", Players), Guard("contracts", Contracts), Guard("clubs", Clubs), Guard("staff", Staff),
        Guard("nations", Nations), Guard("stadiums", Stadiums), Guard("competitions", Competitions),
        Guard("awards", Awards), Guard("cities", Cities), Guard("gameDate", GameDate),
    ];

    static ModuleHealth Guard(string module, Func<(bool Ok, string Detail)> check)
    {
        try { var (ok, detail) = check(); return new(module, ok, detail); }
        catch (Exception ex) { return new(module, false, ex.Message); }
    }

    bool Named(PlayerRecord player) =>
        profile.Fields is { } fields && memory.ReadUInt64(player.Address + (ulong)fields.FullName) != 0;

    static bool Printable(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= 80 && text.All(c => c >= ' ' && c != '\u007f');

    (bool, string) Players()
    {
        var all = new PlayerRepository(memory, profile, moduleBase).LoadAll();
        if (all.Count < 1000) return (false, $"{all.Count} players");
        // Records whose FullName pointer is null are not real players (no name string at all); they also carry a
        // non-contract object in the contract slot, so they must not be counted as offset failures.
        var sample = all.Where(Named).Take(400).ToList();
        var attributes = profile.Attributes.Values.ToList();
        int badId = sample.Count(p => p.Id <= 0 || p.Id > 3_000_000_000);
        int badAbility = sample.Count(p => p.CurrentAbility is < 0 or > 200 || p.PotentialAbility is < 0 or > 200);
        int badName = sample.Count(p => !Printable(p.DisplayName));
        int badAttribute = sample.Count(p => attributes.Any(o => o < profile.PlayerObjectSize &&
            memory.Read(p.Address + (ulong)o, 1)[0] > 100));
        if (badId + badAbility + badName + badAttribute > sample.Count / 20)
            return (false, $"{all.Count} players, bad id {badId}, ability {badAbility}, name {badName}, attribute {badAttribute} of {sample.Count}");
        return (true, $"{all.Count:N0} players");
    }

    (bool, string) Contracts()
    {
        if (profile.Contract is not { } offsets) return (false, "profile has no contract offsets");
        var all = new PlayerRepository(memory, profile, moduleBase).LoadAll();
        var sample = all.Where(Named).Take(400).ToList();
        int checkedContracts = 0, bad = 0;
        foreach (var player in sample)
        {
            ulong contract = memory.ReadUInt64(player.Address + (ulong)offsets.PlayerContract);
            if (contract == 0) continue;
            checkedContracts++;
            if (memory.ReadUInt64(contract) != moduleBase + offsets.ContractVtableRva) bad++;
        }
        if (checkedContracts < 20) return (false, $"only {checkedContracts} contracts found");
        return bad > checkedContracts / 20 ? (false, $"{bad}/{checkedContracts} contract vtables wrong")
            : (true, $"{checkedContracts} contracts");
    }

    (bool, string) Clubs()
    {
        var players = new PlayerRepository(memory, profile, moduleBase).LoadAll();
        var clubs = ClubDirectory.FromPlayers(memory, players, profile, moduleBase);
        if (clubs.Count < 50) return (false, $"{clubs.Count} clubs");
        int bad = clubs.Take(400).Count(c => c.Id <= 0 || !Printable(c.DisplayName));
        return bad > clubs.Count / 20 ? (false, $"{bad}/{clubs.Count} clubs with bad id or name") : (true, $"{clubs.Count:N0} clubs");
    }

    (bool, string) Staff()
    {
        var staff = new StaffRepository(memory, profile, moduleBase).LoadAll();
        if (staff.Count < 200) return (false, $"{staff.Count} staff");
        int bad = staff.Take(400).Count(s => s.Id <= 0 || !Printable(s.DisplayName));
        return bad > staff.Count / 20 ? (false, $"{bad}/{staff.Count} staff with bad id or name") : (true, $"{staff.Count:N0} staff");
    }

    (bool, string) Nations()
    {
        var nations = new NationRepository(memory, profile, moduleBase).LoadAll();
        if (nations.Count < 20) return (false, $"{nations.Count} nations");
        int bad = nations.Count(n => n.Id <= 0 || !Printable(n.DisplayName));
        return bad > nations.Count / 10 ? (false, $"{bad}/{nations.Count} nations with bad id or name") : (true, $"{nations.Count} nations");
    }

    (bool, string) Stadiums()
    {
        var stadiums = new StadiumRepository(memory, profile, moduleBase).LoadAll();
        if (stadiums.Count < 20) return (false, $"{stadiums.Count} stadiums");
        int bad = stadiums.Take(400).Count(s => s.Id <= 0 || !Printable(s.Name));
        return bad > stadiums.Count / 10 ? (false, $"{bad}/{stadiums.Count} stadiums with bad id or name") : (true, $"{stadiums.Count:N0} stadiums");
    }

    (bool, string) Competitions()
    {
        var competitions = new CompetitionRepository(memory, profile, moduleBase).LoadAll();
        if (competitions.Count < 100) return (false, $"{competitions.Count} competitions");
        int bad = competitions.Take(500).Count(c => c.Id <= 0 || !Printable(c.DisplayName));
        return bad > competitions.Count / 10 ? (false, $"{bad}/{competitions.Count} competitions with bad id or name")
            : (true, $"{competitions.Count:N0} competitions");
    }

    (bool, string) Awards()
    {
        var awards = new AwardRepository(memory, profile, moduleBase).LoadAll();
        if (awards.Count < 5) return (false, $"{awards.Count} awards");
        int bad = awards.Count(a => a.Id <= 0 || !Printable(a.DisplayName));
        return bad > awards.Count / 10 ? (false, $"{bad}/{awards.Count} awards with bad id or name") : (true, $"{awards.Count} awards");
    }

    (bool, string) Cities()
    {
        var cities = new CityRepository(memory, profile, moduleBase).LoadAll();
        if (cities.Count < 20) return (false, $"{cities.Count} cities");
        int bad = cities.Take(400).Count(c => c.Id <= 0 || !Printable(c.Name));
        return bad > cities.Count / 10 ? (false, $"{bad}/{cities.Count} cities with bad id or name") : (true, $"{cities.Count:N0} cities");
    }

    (bool, string) GameDate()
    {
        var date = GameClock.Read(memory, profile, moduleBase);
        if (date is null) return (false, "game date unreadable");
        if (date.Value.Year is < 2000 or > 2100) return (false, $"implausible date {date:yyyy-MM-dd}");
        return (true, date.Value.ToString("yyyy-MM-dd"));
    }
}
