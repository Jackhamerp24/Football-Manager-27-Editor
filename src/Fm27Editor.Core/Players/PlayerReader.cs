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
    readonly Dictionary<ulong, string?> _strings = new();
    readonly Dictionary<ulong, ClubInfo?> _clubsByTeam = new();

    public PlayerRecord Decode(ulong address, ReadOnlySpan<byte> obj, uint vtableRva)
    {
        var f = profile.Fields;
        var attrs = new byte[Enum.GetValues<PlayerAttribute>().Length];
        foreach (var (a, off) in profile.Attributes) attrs[(int)a] = obj[off];
        var positions = new byte[Enum.GetValues<PlayerPosition>().Length];
        foreach (var (p, off) in profile.Positions) positions[(int)p] = obj[off];
        var personality = new byte[Enum.GetValues<PersonalityAttribute>().Length];
        foreach (var (p, off) in profile.Personality) personality[(int)p] = obj[off];

        return new PlayerRecord
        {
            Address = address,
            VtableRva = vtableRva,
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
            Contract = profile.Contract is { } c ? ReadContract(U64(obj, c.PlayerContract), c) : null,
            RawAttributes = attrs,
            AttributeScale = profile.AttributeScale,
            Positions = positions,
            Personality = personality,
        };
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

    ContractInfo? ReadContract(ulong address, ContractOffsets c)
    {
        if (address == 0) return null;
        var b = new byte[ContractReadSize];
        if (!memory.TryRead(address, b) || BitConverter.ToUInt64(b) != ModuleBase + c.ContractVtableRva) return null;
        return new ContractInfo(
            address,
            BitConverter.ToInt32(b, c.Wage),
            Date(I16(b, c.ExpiryDate), I16(b, c.ExpiryDate + 2)),
            Date(I16(b, c.JoinedDate), I16(b, c.JoinedDate + 2)),
            ClubOfTeam(BitConverter.ToUInt64(b, c.Team), c));
    }

    ClubInfo? ClubOfTeam(ulong team, ContractOffsets c)
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
                club = new ClubInfo(clubAddress, StringAt(BitConverter.ToUInt64(cb, k.Name)), StringAt(BitConverter.ToUInt64(cb, k.ShortName)));
        }
        _clubsByTeam[team] = club;
        return club;
    }

    /// <summary>Name entry: its first qword points at the string.</summary>
    string? EntryString(ulong entry)
    {
        if (entry == 0) return null;
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(entry, b) ? StringAt(BitConverter.ToUInt64(b)) : null;
    }

    /// <summary>Length-prefixed UTF-8: int32 byte count followed by the bytes.</summary>
    string? StringAt(ulong p)
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
