using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>An xUnit progress sink that runs the callback at once, so a test can cancel from inside a report.</summary>
sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

/// <summary>
/// A Random that answers with the values it was given, in order, and repeats the last one. It refuses an answer
/// outside the range the code asked for, so a test that pins an inclusive bound fails when the bound moves.
/// </summary>
sealed class StubRandom : Random
{
    readonly int[] _values;
    int _at;

    public StubRandom(params int[] values) => _values = values;

    public int Calls { get; private set; }

    /// <summary>
    /// Random.Next(int) does not route through the two-argument overload — measured, not assumed — so the one-argument
    /// form has to be stubbed as well, or a call the code really makes would be answered by the system random source.
    /// </summary>
    public override int Next(int maxValue) => Next(0, maxValue);

    public override int Next(int minValue, int maxValue)
    {
        Calls++;
        int value = _values[Math.Min(_at++, _values.Length - 1)];
        if (value < minValue || value >= maxValue)
            throw new InvalidOperationException($"Asked for {minValue}–{maxValue}, answered {value}");
        return value;
    }
}

/// <summary>Facts a test can make up: whatever continent and youth-contract answer it is given.</summary>
sealed class StubFacts(string? continent, bool? youthContract) : IGoldenGenerationFacts
{
    public string? Continent(PlayerRecord player) => continent;

    public bool? YouthContract(PlayerRecord player) => youthContract;
}

/// <summary>Players the Golden Generation tests are built from; none of it touches a real game.</summary>
static class GoldenGenerationFixtures
{
    /// <summary>A plain player snapshot with no club, division, nationality, contract or dates.</summary>
    public static PlayerRecord Player(int? id,
        ClubInfo? club = null, CompetitionInfo? division = null, NationInfo? nation = null,
        DateOnly? born = null, DateOnly? gameDate = null, ContractInfo? contract = null,
        int ca = 60, int pa = 90, ulong address = 0)
    {
        var at = address == 0 ? (ulong)(0x2000_0000 + (id ?? 0) * 0x400) : address;
        // A player's club sits on its contract, so a club named without a contract has to be given one to reach Player.Club.
        var deal = club is null ? contract : (contract ?? new ContractInfo(at, 0, null, null, null)) with { Club = club };
        return new()
        {
            Address = at,
            VtableRva = TestWorld.PlayerVtable,
            Id = id,
            FirstName = "Player",
            LastName = (id ?? 0).ToString(),
            CurrentAbility = (short)ca,
            PotentialAbility = (short)pa,
            HomeReputation = 0,
            CurrentReputation = 0,
            WorldReputation = 0,
            Height = 180,
            DateOfBirth = born,
            GameDate = gameDate,
            Nationality = nation,
            Division = division,
            Contract = deal,
            RawAttributes = new byte[Enum.GetValues<PlayerAttribute>().Length],
            AttributeScale = 5,
            Positions = new byte[Enum.GetValues<PlayerPosition>().Length],
            Personality = new byte[Enum.GetValues<PersonalityAttribute>().Length],
        };
    }

    public static ClubInfo Club(string name, ulong address) => new(address, name, name);

    public static CompetitionInfo Division(string name, ulong address) => new(address, name, name);

    public static NationInfo Nation(string name, int? id, ulong address) => new(address, name, name, id);

    /// <summary>Maps a player object into the fake game and returns the snapshot of it.</summary>
    public static PlayerRecord Spawn(TestWorld world, int id, int ca, int pa, ulong address,
        DateOnly? born = null, DateOnly? gameDate = null, ClubInfo? club = null, NationInfo? nation = null,
        CompetitionInfo? division = null, ContractInfo? contract = null)
    {
        var data = world.Memory.Map(address, 0x3C8);
        TestWorld.Set64(data, 0x000, TestWorld.ModuleBase + TestWorld.PlayerVtable);
        TestWorld.Set16(data, 0x264, (short)ca);   // Current Ability
        TestWorld.Set16(data, 0x266, (short)pa);   // Potential Ability
        BitConverter.GetBytes(id).CopyTo(data, 0x294);
        return Player(id, club, division, nation, born, gameDate, contract, ca, pa, address);
    }
}
