using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Contract layout measured on FM27 27.0.0 (Acerbi at PSG, checked in game): wage int32 +0x20, expiry +0x48,
/// agreed playing time byte +0x54 (Squad Player = 4, Impact Sub = 5, as the game UI showed after the write).
/// </summary>
public class ContractWriterTests
{
    const ulong Contract = 0x4000_0000;
    const uint ContractVtable = 0x4B00AE8;
    readonly TestWorld _w = new();

    public ContractWriterTests()
    {
        var c = _w.Memory.Map(Contract, 0xC8);
        TestWorld.Set64(c, 0x00, TestWorld.ModuleBase + ContractVtable);
        BitConverter.GetBytes(40130).CopyTo(c, 0x20);
        TestWorld.Set16(c, 0x48, 182); TestWorld.Set16(c, 0x4A, 2028); // 30 June 2028, leap year: day 182
        c[0x54] = 4;
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x330, BitConverter.GetBytes(Contract)));
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    ContractWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void ContractSearchUsesObservedTypeAndPlayingTimeAndReportsStaleContracts()
    {
        Assert.True(_w.Memory.TryWrite(Contract + 0xC3, [0]));
        var player = Player();
        var search = new ContractSearch(Writer());
        var match = search.Find([player], new(ContractType.PartTime, PlayingTime.SquadPlayer));
        Assert.Equal(new[] { player.Address }, match.Matches.Select(p => p.Address));
        Assert.Empty(match.Unavailable);
        Assert.Empty(search.Find([player], new(ContractType.Youth, PlayingTime.SquadPlayer)).Matches);
        Assert.Empty(search.Find([player], new(ContractType.PartTime, PlayingTime.ImpactSub)).Matches);
        Assert.True(_w.Memory.TryWrite(Contract, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1111110)));
        var stale = search.Find([player], new(ContractType.PartTime, PlayingTime.SquadPlayer));
        Assert.Empty(stale.Matches);
        Assert.Equal(new[] { player.Address }, stale.Unavailable.Select(p => p.Address));
    }

    [Fact]
    public void ContractSearchAppliesInclusiveWageAndAskingPriceRanges()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x234, BitConverter.GetBytes(1234567)));
        var player = Player();
        var search = new ContractSearch(Writer());
        Assert.Equal(new[] { player.Address }, search.Find([player], new(MinimumWage: 40130, MaximumWage: 40130,
            MinimumAskingPrice: 1234567, MaximumAskingPrice: 1234567)).Matches.Select(p => p.Address));
        Assert.Empty(search.Find([player], new(MinimumWage: 40131)).Matches);
        Assert.Empty(search.Find([player], new(MaximumAskingPrice: 1234566)).Matches);
        Assert.Throws<ArgumentException>(() => search.Find([player], new(MinimumWage: 5, MaximumWage: 4)));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x234, BitConverter.GetBytes(-1)));
        Assert.Equal(new[] { player.Address }, search.Find([Player()], new(MinimumAskingPrice: 0)).Unavailable.Select(p => p.Address));
    }

    [Fact]
    public void ContractSearchDistinguishesObservedClauseFromInvalidVector()
    {
        // FMRTE recorded Yearly Wage Rise as type 3, value 7, parameter -1.
        const ulong term = 0x4000_2000;
        var bytes = _w.Memory.Map(term, 8);
        BitConverter.GetBytes(7).CopyTo(bytes, 0);
        TestWorld.Set16(bytes, 4, -1); TestWorld.Set16(bytes, 6, 3);
        Assert.True(_w.Memory.TryWrite(Contract + 0x68, [.. BitConverter.GetBytes(term), .. BitConverter.GetBytes(term + 8), .. BitConverter.GetBytes(term + 8)]));
        var player = Player();
        var search = new ContractSearch(Writer());
        Assert.Equal(new[] { player.Address }, search.Find([player], new(ClauseType: 3, HasClause: true, MinimumClauseValue: 7, MaximumClauseValue: 7)).Matches.Select(p => p.Address));
        Assert.Empty(search.Find([player], new(ClauseType: 3, HasClause: false)).Matches);
        Assert.Empty(search.Find([player], new(ClauseType: 3, MinimumClauseValue: 8)).Matches);
        Assert.True(_w.Memory.TryWrite(Contract + 0x70, BitConverter.GetBytes(term)));
        Assert.Single(search.Find([player], new(ClauseType: 3, HasClause: false)).Matches);
        Assert.True(_w.Memory.TryWrite(Contract + 0x70, BitConverter.GetBytes(term + 9)));
        var invalid = search.Find([player], new(ClauseType: 3, HasClause: false));
        Assert.Empty(invalid.Matches);
        Assert.Equal(new[] { player.Address }, invalid.Unavailable.Select(p => p.Address));
    }

    OffsetProfile ExtendedProfile()
    {
        const string anchor = "\"playingTime\": \"0x54\"";
        string json = _w.Profile.ToJson().Replace(anchor,
            anchor + ", \"previousPlayingTime\": \"0x55\", \"loanStatusOffset\": \"0x57\", \"loanStatusMask\": \"0x2\"");
        return OffsetProfile.Load(json);
    }

    [Fact]
    public void PreviousPlayingTimeAndLoanListedRoundTripThroughMappedBytes()
    {
        // Yoro (Man Utd save, FMRTE Contract tab): Previous = Unset, Loan Status = Listed.
        var profile = ExtendedProfile();
        var writer = new ContractWriter(_w.Memory, profile, TestWorld.ModuleBase);
        Assert.True(_w.Memory.TryWrite(Contract + 0x55, [(byte)PlayingTime.BreakthroughProspect]));
        Assert.True(_w.Memory.TryWrite(Contract + 0x57, [0x42]));
        var player = new PlayerRepository(_w.Memory, profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var details = writer.Read(player)!;
        Assert.Equal(PlayingTime.BreakthroughProspect, details.PreviousPlayingTime);
        Assert.True(details.LoanListed);

        writer.SetPreviousPlayingTime(player, details, PlayingTime.Star);
        Assert.Equal((byte)PlayingTime.Star, _w.Bytes(Contract, 0x60)[0x55]);
        var refreshed = writer.Read(new PlayerRepository(_w.Memory, profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!)!;
        writer.SetLoanListed(player, refreshed, false);
        Assert.Equal(0x40, _w.Bytes(Contract, 0x60)[0x57]); // only the loan bit cleared
        var search = new ContractSearch(writer);
        Assert.Empty(search.Find([player], new(LoanListed: true)).Matches);
        Assert.Equal(new[] { player.Address }, search.Find([player], new(LoanListed: false)).Matches.Select(p => p.Address));
    }

    [Fact]
    public void UnmappedBuildsReportNullAndRefuseWrites()
    {
        var details = Writer().Read(Player())!;
        Assert.Null(details.PreviousPlayingTime);
        Assert.Null(details.LoanListed);
        Assert.Throws<InvalidOperationException>(() => Writer().SetPreviousPlayingTime(Player(), details, PlayingTime.Star));
        Assert.Throws<InvalidOperationException>(() => Writer().SetLoanListed(Player(), details, true));
    }

    [Fact]
    public void Writes_wage_playing_time_and_expiry_only_in_their_fields()
    {
        var before = _w.Bytes(Contract, 0x60);
        Writer().SetWage(Player(), 55555);
        Writer().SetPlayingTime(Player(), PlayingTime.ImpactSub);
        Writer().SetExpiry(Player(), new DateOnly(2030, 6, 30));
        var after = _w.Bytes(Contract, 0x60);

        Assert.Equal(BitConverter.GetBytes(55555), after[0x20..0x24]);
        Assert.Equal(5, after[0x54]);
        Assert.Equal(new byte[] { 181, 0, 0xEE, 0x07 }, after[0x48..0x4C]); // 30 June 2030 = day 181, 2030 = 0x7EE
        after[0x20..0x24].CopyTo(before, 0x20); before[0x54] = after[0x54]; after[0x48..0x4C].CopyTo(before, 0x48);
        Assert.Equal(before, after);
    }

    // Contract type is the byte at +0xC3 (FM26's ContractType offset): over 43,000 contracts on 15/8/2026 +0x26 was almost always 1
    // while +0xC3 varied, and FMRTE 27 showed Roderick Briffa (0) Part Time, Ibrahim Dosso (3) Youth, Omar Marmoush's offered
    // contract (1) Full Time and David Silva (4) blank. FMRTE writing Part Time on Marmoush's offered contract left +0xC3 = 0.
    [Fact]
    public void Type_is_the_byte_at_0xC3()
    {
        Assert.True(_w.Memory.TryWrite(Contract + 0x24, [0x0B, 0x00, 0x01, 0x00]));
        Assert.True(_w.Memory.TryWrite(Contract + 0xC3, [0]));
        var s = Writer().Read(Player())!;
        Assert.Equal(ContractType.PartTime, s.Type);
        var before = _w.Bytes(Contract, 0xC8);
        Writer().SetType(Player(), s, ContractType.Youth);
        before[0xC3] = 3;
        Assert.Equal(before, _w.Bytes(Contract, 0xC8));
        Assert.Equal(ContractType.Youth, Writer().Read(Player())!.Type);
    }

    [Fact]
    public void Shift_expiry_moves_by_whole_years_and_keeps_the_day()
    {
        Writer().ShiftExpiry(Player(), 1);
        // 30 June 2029 (non-leap) = day 181, 2029 = 0x07ED.
        Assert.Equal(new byte[] { 181, 0, 0xED, 0x07 }, _w.Bytes(Contract + 0x48, 4));
        Writer().ShiftExpiry(Player(), -2);
        // 30 June 2027 = day 181, 2027 = 0x07EB.
        Assert.Equal(new byte[] { 181, 0, 0xEB, 0x07 }, _w.Bytes(Contract + 0x48, 4));
        Assert.Equal(new DateOnly(2027, 6, 30), Player().Contract!.Expires);
    }

    /// <summary>User's rule: Renew for 4 Years counts from the game date (22/7/2026 in the test save), not from the expiry.</summary>
    [Fact]
    public void Renew_counts_from_the_game_date()
    {
        Writer().RenewForYears(Player(), 4, new DateOnly(2026, 7, 22));
        // 22 July 2030 = day 203 (non-leap: 181 + 22), 2030 = 0x07EE.
        Assert.Equal(new byte[] { 203, 0, 0xEE, 0x07 }, _w.Bytes(Contract + 0x48, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().RenewForYears(Player(), 0, new DateOnly(2026, 7, 22)));
    }

    /// <summary>User's rule: Make it Expire sets the expiry to the game date.</summary>
    [Fact]
    public void Make_expire_sets_the_game_date()
    {
        Writer().MakeExpire(Player(), new DateOnly(2026, 7, 22));
        // 22 July 2026 = day 203, 2026 = 0x07EA.
        Assert.Equal(new byte[] { 203, 0, 0xEA, 0x07 }, _w.Bytes(Contract + 0x48, 4));
    }

    /// <summary>"Apply to all" shifts whatever the game holds now, not an old snapshot: re-read, then a guarded write.</summary>
    [Fact]
    public void Shift_expiry_uses_the_current_date_when_the_game_changed_it()
    {
        var snapshot = Player();
        // The game moved the expiry to day 100 of 2028 (9 April, leap year) after the snapshot was taken.
        Assert.True(_w.Memory.TryWrite(Contract + 0x48, BitConverter.GetBytes((short)100)));
        Writer().ShiftExpiry(snapshot, 1);
        // 9 April 2029 = day 99 (non-leap), 2029 = 0x07ED.
        Assert.Equal(new byte[] { 99, 0, 0xED, 0x07 }, _w.Bytes(Contract + 0x48, 4));
    }

    /// <summary>
    /// FMRTE 27, Man Utd: Tielemans Number 18, Joined Club 13/7/2026 (player + 0x3A4 raw C200EA07). Writing 20/7/2026 to
    /// player + 0x3A4 changed FMRTE's Joined Club only; the number is one signed byte at contract + 0x5D (-1 = none).
    /// </summary>
    [Fact]
    public void Squad_number_is_a_byte_and_joined_club_lives_in_the_player()
    {
        Assert.True(_w.Memory.TryWrite(Contract + 0x5C, [10, 18, 0]));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x3A4, Convert.FromHexString("C200EA07")));
        var d = Writer().Read(Player())!;
        Assert.Equal(18, d.SquadNumber);
        Assert.Equal(new DateOnly(2026, 7, 13), d.JoinedClub);

        var contractBefore = _w.Bytes(Contract, 0xC0);
        Writer().SetJoinedClub(Player(), d, new DateOnly(2026, 7, 20));
        Assert.Equal(Convert.FromHexString("C900EA07"), _w.Bytes(TestWorld.Mainoo + 0x3A4, 4));
        Assert.Equal(contractBefore, _w.Bytes(Contract, 0xC0));

        Writer().SetSquadNumber(Player(), Writer().Read(Player())!, -1);
        Assert.Equal(new byte[] { 10, 0xFF, 0 }, _w.Bytes(Contract + 0x5C, 3));
        Assert.Equal(-1, Writer().Read(Player())!.SquadNumber);
    }

    [Fact]
    public void Start_date_keeps_the_flag_bits_above_the_day()
    {
        // Acerbi in FM27: start 0x3CCB = flags 0x3C00 | day 0xCB (203 = 22 July 2026).
        Assert.True(_w.Memory.TryWrite(Contract + 0x44, [0xCB, 0x3C, 0xEA, 0x07]));
        var d = Writer().Read(Player())!;
        Assert.Equal(new DateOnly(2026, 7, 22), d.Started);

        Writer().SetStarted(Player(), d, new DateOnly(2027, 1, 2));
        Assert.Equal(new byte[] { 0x02, 0x3C, 0xEB, 0x07 }, _w.Bytes(Contract + 0x44, 4)); // day 2, flags kept, 2027
    }

    [Fact]
    public void Edits_a_clause_value_and_parameter_but_keeps_its_type()
    {
        // Clause vector of Lacey's FM26 contract: wage after 15 club league games -> 25,767 (type 13).
        const ulong Terms = 0x4000_1000;
        var t = _w.Memory.Map(Terms, 8);
        BitConverter.GetBytes(25767).CopyTo(t, 0); TestWorld.Set16(t, 4, 15); TestWorld.Set16(t, 6, 13);
        Assert.True(_w.Memory.TryWrite(Contract + 0x68, BitConverter.GetBytes(Terms)));
        Assert.True(_w.Memory.TryWrite(Contract + 0x70, BitConverter.GetBytes(Terms + 8)));
        var d = Writer().Read(Player())!;
        Assert.Equal(new ContractTerm(0, 25767, 15, 13), Assert.Single(d.Terms));

        Writer().SetTerm(Player(), d, 0, 30000, 10);
        Assert.Equal(new ContractTerm(0, 30000, 10, 13), Assert.Single(Writer().Read(Player())!.Terms));
    }

    const ulong TermBuffer = 0x4000_2000;

    /// <summary>Lacey's three FM26 bonuses: appearance 4,771 (32), assist 2,982 (39), unused sub 3,181 (38); room for <paramref name="capacity"/>.</summary>
    void LaceyTerms(int capacity)
    {
        var t = _w.Memory.Map(TermBuffer, capacity * 8);
        (int v, short p, short ty)[] items = [(4771, -1, 32), (2982, -1, 39), (3181, -1, 38)];
        for (int i = 0; i < items.Length; i++)
        { BitConverter.GetBytes(items[i].v).CopyTo(t, i * 8); TestWorld.Set16(t, i * 8 + 4, items[i].p); TestWorld.Set16(t, i * 8 + 6, items[i].ty); }
        Assert.True(_w.Memory.TryWrite(Contract + 0x68, [.. BitConverter.GetBytes(TermBuffer), .. BitConverter.GetBytes(TermBuffer + 24), .. BitConverter.GetBytes(TermBuffer + (ulong)capacity * 8)]));
    }

    [Fact]
    public void ContractSearchRequiresAllSelectedClauses()
    {
        LaceyTerms(capacity: 3); // FMRTE fixture: 32=4771, 39=2982, 38=3181.
        var player = Player(); var search = new ContractSearch(Writer());
        var criteria = new ContractSearchCriteria { Clauses = [new(32, true, 4771, 4771), new(39, true, 2982, 2982), new(3, false)] };
        Assert.Equal(new[] { player.Address }, search.Find([player], criteria).Matches.Select(p => p.Address));
        Assert.Empty(search.Find([player], criteria with { Clauses = [new(32, true, 4771, 4771), new(39, true, 3000, null)] }).Matches);
        Assert.Empty(search.Find([player], criteria with { Clauses = [new(32, false), new(39, true)] }).Matches);
    }

    [Fact]
    public void Adds_a_clause_in_place_when_the_list_has_room()
    {
        LaceyTerms(capacity: 4);
        Writer().AddTerm(Player(), Writer().Read(Player())!, 22, -1, 1, allocate: null);
        var terms = Writer().Read(Player())!.Terms;
        Assert.Equal(4, terms.Count);
        Assert.Equal(new ContractTerm(3, -1, 1, 22), terms[3]);
        Assert.Equal(TermBuffer, BitConverter.ToUInt64(_w.Bytes(Contract + 0x68, 8)));
    }

    [Fact]
    public void Adds_a_clause_into_a_new_buffer_when_the_list_is_full()
    {
        LaceyTerms(capacity: 3);
        const ulong Grown = 0x4100_0000;
        Writer().AddTerm(Player(), Writer().Read(Player())!, 13, 25767, 15, bytes => { _w.Memory.Map(Grown, bytes); return Grown; });
        Assert.Equal(new[] { Grown, Grown + 32, Grown + 48 }, Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(_w.Bytes(Contract + 0x68 + (ulong)i * 8, 8))));
        Assert.Equal(new[] { 4771, 2982, 3181, 25767 }, Writer().Read(Player())!.Terms.Select(t => t.Value));
    }

    [Fact]
    public void Removing_a_clause_moves_the_later_ones_up()
    {
        LaceyTerms(capacity: 3);
        Writer().RemoveTerm(Player(), Writer().Read(Player())!, 0);
        var terms = Writer().Read(Player())!.Terms;
        Assert.Equal(new short[] { 39, 38 }, terms.Select(t => t.Type));
        Assert.Equal(new[] { 2982, 3181 }, terms.Select(t => t.Value));
    }

    [Fact]
    public void Refuses_a_second_option_clause_of_the_same_type()
    {
        LaceyTerms(capacity: 4);
        Assert.Throws<InvalidOperationException>(() => Writer().AddTerm(Player(), Writer().Read(Player())!, 32, 5000, -1, null));
    }

    [Fact]
    public void Refuses_when_the_contract_object_is_no_longer_a_contract()
    {
        var p = Player();
        Assert.True(_w.Memory.TryWrite(Contract, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234)));
        Assert.Throws<StaleObjectException>(() => Writer().SetWage(p, 55555));
        Assert.Equal(BitConverter.GetBytes(40130), _w.Bytes(Contract + 0x20, 4));
    }

    [Fact]
    public void Refuses_when_the_wage_changed_since_the_snapshot()
    {
        var p = Player();
        Assert.True(_w.Memory.TryWrite(Contract + 0x20, BitConverter.GetBytes(41000)));
        Assert.Throws<StaleObjectException>(() => Writer().SetWage(p, 55555));
        Assert.Equal(BitConverter.GetBytes(41000), _w.Bytes(Contract + 0x20, 4));
    }
}
