using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>Decodes player objects. Name strings are shared between players, so they are cached by address.</summary>
public sealed class PlayerReader(IProcessMemory memory, OffsetProfile profile)
{
    const int MaxNameLength = 256;
    const int ContractReadSize = 0x60;
    const int TeamReadSize = 0x40;
    const int ClubReadSize = 0xD0;
    const int NationReadSize = 0x38;
    const int CompetitionReadSize = 0x50;
    readonly Dictionary<ulong, string?> _strings = new();
    readonly Dictionary<ulong, ClubInfo?> _clubsByTeam = new();
    readonly Dictionary<ulong, NationInfo?> _nations = new();
    readonly Dictionary<ulong, CompetitionInfo?> _divisionsByTeam = new();
    readonly Dictionary<ulong, CompetitionInfo?> _competitions = new();

    public PlayerRecord Decode(ulong address, ReadOnlySpan<byte> obj, uint vtableRva)
    {
        var f = profile.Fields;
        var attrs = new byte[Enum.GetValues<PlayerAttribute>().Length];
        foreach (var (a, off) in profile.Attributes) attrs[(int)a] = obj[off];
        var positions = new byte[Enum.GetValues<PlayerPosition>().Length];
        foreach (var (p, off) in profile.Positions) positions[(int)p] = obj[off];
        var personality = new byte[Enum.GetValues<PersonalityAttribute>().Length];
        foreach (var (p, off) in profile.Personality) personality[(int)p] = obj[off];

        ulong contractTeam = 0;
        var contract = profile.Contract is { } c ? ReadContract(U64(obj, c.PlayerContract), c, out contractTeam) : null;
        ulong playingTeam = profile.Team is { } tm ? U64(obj, tm.PlayerTeam) : 0;
        CompetitionInfo? division = null;
        if (profile.Competition is { } comp)
            division = DivisionOfTeam(playingTeam, comp) ?? DivisionOfTeam(contractTeam, comp);

        return new PlayerRecord
        {
            Address = address,
            VtableRva = vtableRva,
            Id = f.Id is { } id ? BitConverter.ToInt32(obj[id..]) : null,
            PersonIndex = f.PersonIndex is { } pix ? BitConverter.ToInt32(obj[pix..]) : null,
            FullName = StringAt(U64(obj, f.FullName)),
            FirstName = EntryString(U64(obj, f.FirstName)),
            LastName = EntryString(U64(obj, f.LastName)),
            CommonName = EntryString(U64(obj, f.CommonName)),
            CurrentAbility = I16(obj, f.CurrentAbility),
            PotentialAbility = I16(obj, f.PotentialAbility),
            HomeReputation = I16(obj, f.HomeReputation),
            CurrentReputation = I16(obj, f.CurrentReputation),
            WorldReputation = I16(obj, f.WorldReputation),
            Height = I16(obj, f.Height),
            DateOfBirth = Date(I16(obj, f.DateOfBirth), I16(obj, f.DateOfBirth + 2)),
            Gender = GenderReader.FromPlayerBytes(obj, profile.Relations),
            Contract = contract,
            Nationality = profile.Nation is { } nat ? ReadNation(U64(obj, nat.PlayerNation), nat) : null,
            Division = division,
            AskingPrice = ReadDirect(PlayerField.AskingPrice, obj),
            InternationalCaps = ReadDirect(PlayerField.Caps, obj),
            InternationalGoals = ReadDirect(PlayerField.InternationalGoals, obj),
            Ethnicity = ReadDirect(PlayerField.Ethnicity, obj),
            TreatedAsNonEuInItaly = ReadDirect(PlayerField.TreatedAsNonEuInItaly, obj),
            PreferredMoveBits = profile.PreferredMoves is { } moveOffset && moveOffset >= 0 && moveOffset <= obj.Length - sizeof(ulong)
                ? BitConverter.ToUInt64(obj[moveOffset..]) : null,
            PendingContractAddress = profile.PendingContract is { } pc ? U64(obj, pc.PlayerPointer) : 0,
            RawAttributes = attrs,
            AttributeScale = profile.AttributeScale,
            Positions = positions,
            Personality = personality,
        };
    }

    long? ReadDirect(PlayerField field, ReadOnlySpan<byte> obj)
    {
        if (profile.PlayerFields is not { } fields || !fields.TryGetValue(field, out var offset) || offset.Path.Count != 1) return null;
        int at = offset.Path[0];
        int size = Fm27Editor.Core.Clubs.ClubFieldAccess.Size(offset.Type);
        if (at < 0 || at > obj.Length - size) return null;
        return Fm27Editor.Core.Clubs.ClubFieldAccess.Decode(obj.Slice(at, size).ToArray(), offset);
    }

    static ulong U64(ReadOnlySpan<byte> b, int off) => BitConverter.ToUInt64(b[off..]);
    static short I16(ReadOnlySpan<byte> b, int off) => BitConverter.ToInt16(b[off..]);

    static DateOnly? Date(short dayOfYear, short year)
    {
        if (year < 1800 || year > 2300 || dayOfYear < 1 || dayOfYear > 366) return null;
        var d = new DateOnly(year, 1, 1).AddDays(dayOfYear - 1);
        return d.Year == year ? d : null;
    }

    /// <summary>Module base, needed to check contract and club vtables. Set by the repository.</summary>
    public ulong ModuleBase { get; init; }

    ContractInfo? ReadContract(ulong address, ContractOffsets c, out ulong teamAddress)
    {
        teamAddress = 0;
        if (address == 0) return null;
        var b = new byte[ContractReadSize];
        if (!memory.TryRead(address, b) || BitConverter.ToUInt64(b) != ModuleBase + c.ContractVtableRva) return null;
        teamAddress = BitConverter.ToUInt64(b, c.Team);
        return new ContractInfo(
            address,
            BitConverter.ToInt32(b, c.Wage),
            // Contract dates keep flags above the 9-bit day of year.
            Date((short)(I16(b, c.ExpiryDate) & 0x1FF), I16(b, c.ExpiryDate + 2)),
            Date((short)(I16(b, c.JoinedDate) & 0x1FF), I16(b, c.JoinedDate + 2)),
            ClubOfTeam(teamAddress, c));
    }

    internal NationInfo? ReadNation(ulong address, NationOffsets nat)
    {
        if (address == 0) return null;
        if (_nations.TryGetValue(address, out var cached)) return cached;
        NationInfo? nation = null;
        int size = Math.Max(NationReadSize, Math.Max(nat.Name, nat.ShortName) + 8);
        var b = new byte[size];
        if (memory.TryRead(address, b) && BitConverter.ToUInt64(b) == ModuleBase + nat.VtableRva)
        {
            int? natId = nat.Id is { } idOff && idOff + 4 <= b.Length ? BitConverter.ToInt32(b, idOff) : null;
            if (natId <= 0) natId = null;
            nation = new NationInfo(address, StringAt(BitConverter.ToUInt64(b, nat.Name)), StringAt(BitConverter.ToUInt64(b, nat.ShortName)), natId);
        }
        _nations[address] = nation;
        return nation;
    }

    CompetitionInfo? DivisionOfTeam(ulong team, CompetitionOffsets comp)
    {
        if (team == 0) return null;
        if (_divisionsByTeam.TryGetValue(team, out var cached)) return cached;
        CompetitionInfo? division = null;
        Span<byte> ptrBuf = stackalloc byte[8];
        if (memory.TryRead(team + (ulong)comp.TeamCompetition, ptrBuf))
        {
            ulong compAddr = BitConverter.ToUInt64(ptrBuf);
            division = ReadCompetition(compAddr, comp);
        }
        _divisionsByTeam[team] = division;
        return division;
    }

    CompetitionInfo? ReadCompetition(ulong address, CompetitionOffsets comp)
    {
        if (address == 0) return null;
        if (_competitions.TryGetValue(address, out var cached)) return cached;
        CompetitionInfo? competition = null;
        int size = Math.Max(CompetitionReadSize, Math.Max(comp.Name, comp.ShortName) + 8);
        var b = new byte[size];
        if (memory.TryRead(address, b) && BitConverter.ToUInt64(b) == ModuleBase + comp.VtableRva)
            competition = new CompetitionInfo(address, StringAt(BitConverter.ToUInt64(b, comp.Name)), StringAt(BitConverter.ToUInt64(b, comp.ShortName)));
        _competitions[address] = competition;
        return competition;
    }

    internal ClubInfo? ClubOfTeam(ulong team, ContractOffsets c)
    {
        if (team == 0 || profile.Club is not { } k) return null;
        if (_clubsByTeam.TryGetValue(team, out var cached)) return cached;
        ClubInfo? club = null;
        var t = new byte[TeamReadSize];
        if (memory.TryRead(team, t))
        {
            ulong clubAddress = BitConverter.ToUInt64(t, c.TeamClub);
            var cb = new byte[ClubReadSize];
            if (clubAddress != 0 && memory.TryRead(clubAddress, cb) && BitConverter.ToUInt64(cb) == ModuleBase + k.ClubVtableRva)
                club = new ClubInfo(clubAddress, StringAt(BitConverter.ToUInt64(cb, k.Name)), StringAt(BitConverter.ToUInt64(cb, k.ShortName)),
                    GenderReader.FromClubBytes(cb, k.Gender));
        }
        _clubsByTeam[team] = club;
        return club;
    }

    /// <summary>Name entry: its first qword points at the string.</summary>
    internal string? EntryString(ulong entry)
    {
        if (entry == 0) return null;
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(entry, b) ? StringAt(BitConverter.ToUInt64(b)) : null;
    }

    /// <summary>Length-prefixed UTF-8: int32 byte count followed by the bytes.</summary>
    internal string? StringAt(ulong p)
    {
        if (p == 0) return null;
        if (_strings.TryGetValue(p, out var cached)) return cached;
        string? result = null;
        Span<byte> len = stackalloc byte[4];
        if (memory.TryRead(p, len))
        {
            int n = BitConverter.ToInt32(len);
            if (n > 0 && n <= MaxNameLength)
            {
                var bytes = new byte[n];
                if (memory.TryRead(p + 4, bytes)) result = Encoding.UTF8.GetString(bytes);
            }
            else if (n == 0) result = "";
        }
        _strings[p] = result;
        return result;
    }
}
