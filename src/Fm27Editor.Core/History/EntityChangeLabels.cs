using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Officials;

namespace Fm27Editor.Core.History;

/// <summary>Names the bytes of a club or competition for the edit history, from the profile's own offsets.</summary>
public static class EntityChangeLabels
{
    public static Func<ulong, int, ChangeLabel?> ForReferee(ulong referee)
    {
        var fields = new (int Offset, string Name)[]
        {
            (RefereeDirectory.CurrentAbility, "Current Ability"), (RefereeDirectory.PotentialAbility, "Potential Ability"),
            (RefereeDirectory.Reputation, "Reputation"), (RefereeDirectory.AllowingFlow, "Allowing Flow"),
            (RefereeDirectory.Discipline, "Discipline"), (RefereeDirectory.ImportantMatches, "Important Matches"),
            (RefereeDirectory.Pressure, "Pressure"), (RefereeDirectory.Refereeing, "Refereeing"),
            (RefereeDirectory.RunningLine, "Running Line"), (RefereeDirectory.TimeKeeping, "Time Keeping"),
            (RefereeDirectory.EliteList, "Elite List"), (RefereeDirectory.Continental, "Continental"),
            (RefereeDirectory.EuropeanCategory, "European Category"),
        };
        var map = fields.ToDictionary(x => referee + (ulong)x.Offset, x => new ChangeLabel(x.Name));
        return (address, _) => map.GetValueOrDefault(address);
    }
    /// <summary>
    /// Labeler for a club and its team: every field the profile maps (Information, Finances, Supporters, Tactical
    /// Understanding...) at the address it resolves to right now.
    /// </summary>
    public static Func<ulong, int, ChangeLabel?> ForClub(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, ulong club, ulong team)
    {
        var access = new ClubFieldAccess(memory, profile, moduleBase);
        var byAddress = new Dictionary<ulong, ChangeLabel>();
        foreach (var (field, offset) in profile.Club?.Fields ?? new Dictionary<ClubField, ClubFieldOffset>())
        {
            ulong root = offset.Root == ClubFieldRoot.Club ? club : team;
            if (root == 0 || access.AddressOf(root, field) is not { } at) continue;
            byAddress[at] = Label(PlayerChangeLabels.Words(field.ToString()), offset);
        }
        return (address, _) => byAddress.TryGetValue(address, out var label) ? label : null;
    }

    /// <summary>How a mapped field's bytes read: dates, colours and floats are not plain numbers.</summary>
    internal static ChangeLabel Label(string name, ClubFieldOffset offset)
    {
        var kind = offset.Type switch
        {
            ClubFieldType.Date => ChangeValueKind.Date, ClubFieldType.Colour => ChangeValueKind.Colour,
            ClubFieldType.Float => ChangeValueKind.Float, ClubFieldType.SByte => ChangeValueKind.Signed, _ => ChangeValueKind.Number,
        };
        bool raw = kind != ChangeValueKind.Number || offset.Type == ClubFieldType.Bool || offset.Mask != 0;
        return new ChangeLabel(name, raw ? 1 : Math.Max(1, offset.Scale), 1, kind);
    }

    public static Func<ulong, int, ChangeLabel?> ForAward(OffsetProfile profile, ulong award)
    {
        var map = new Dictionary<ulong, ChangeLabel>();
        if (profile.Award is { } a)
        {
            map[award + (ulong)a.Reputation] = new("Reputation");
            map[award + (ulong)a.WinnerHomeReputation] = new("Winner Home Reputation");
            map[award + (ulong)a.WinnerWorldReputation] = new("Winner World Reputation");
        }
        return (address, _) => map.GetValueOrDefault(address);
    }

    public static Func<ulong, int, ChangeLabel?> ForCity(OffsetProfile profile, ulong city)
    {
        var map = new Dictionary<ulong, ChangeLabel>();
        if (profile.Cities?.Fields is { } f)
        {
            map[city + (ulong)f.Latitude] = new("Latitude", Kind: ChangeValueKind.Float);
            map[city + (ulong)f.Longitude] = new("Longitude", Kind: ChangeValueKind.Float);
            map[city + (ulong)f.Altitude] = new("Altitude");
            map[city + (ulong)f.Inhabitants] = new("Inhabitants Range");
            map[city + (ulong)f.Attraction] = new("Attraction");
        }
        return (address, _) => map.GetValueOrDefault(address);
    }

    /// <summary>Stadium: mapped fields, the two flag bytes (by checkbox), seat colour, international pitch size and the eight stands.</summary>
    public static Func<ulong, int, ChangeLabel?> ForStadium(IProcessMemory memory, OffsetProfile profile, ulong stadium)
    {
        var map = new Dictionary<ulong, ChangeLabel>();
        if (profile.Stadium is { } s)
        {
            foreach (var (field, offset) in s.Fields)
                if (offset.Path.Count == 1) map[stadium + (ulong)offset.Path[0]] = Label(PlayerChangeLabels.Words(field.ToString()), offset);
            string[] Bits(IReadOnlyDictionary<Fm27Editor.Core.Stadiums.StadiumFlag, byte> bits)
            {
                var names = new string[8];
                foreach (var (flag, mask) in bits)
                    for (int i = 0; i < 8; i++) if (mask == 1 << i) names[i] = PlayerChangeLabels.Words(flag.ToString());
                return names;
            }
            map[stadium + (ulong)s.Flags] = new("Options", Kind: ChangeValueKind.Flags, PartNames: Bits(s.FlagBits));
            if (s.AltFlags >= 0) map[stadium + (ulong)s.AltFlags] = new("Options", Kind: ChangeValueKind.Flags, PartNames: Bits(s.AltFlagBits));
            if (s.SeatColour >= 0) map[stadium + (ulong)s.SeatColour] = new("Seat Colour", Kind: ChangeValueKind.Colour);
            if (s.IntLength >= 0) map[stadium + (ulong)s.IntLength] = new("International Pitch Length", 10);
            if (s.IntWidth >= 0) map[stadium + (ulong)s.IntWidth] = new("International Pitch Width", 10);
            map[stadium + (ulong)s.Latitude] = new("Latitude", Kind: ChangeValueKind.Float);
            map[stadium + (ulong)s.Longitude] = new("Longitude", Kind: ChangeValueKind.Float);
            if (memory.TryRead(stadium + (ulong)s.Stands, new byte[8]) && memory.ReadUInt64(stadium + (ulong)s.Stands) is var stands and not 0)
            {
                string[] standBits = new string[8];
                for (int i = 0; i < 8; i++)
                    standBits[i] = (1 << i) == s.RoofBit ? "Roof" : (1 << i) == s.SeatsBit ? "Seats" : (1 << i) == s.TunnelBit ? "Tunnel" : "";
                for (int i = 0; i < s.StandCount; i++)
                {
                    map[stands + (ulong)(i * s.StandSize)] = new($"Stand {i + 1} Capacity");
                    map[stands + (ulong)(i * s.StandSize + s.StandFlags)] = new($"Stand {i + 1}", Kind: ChangeValueKind.Flags, PartNames: standBits);
                }
            }
        }
        return (address, _) => map.GetValueOrDefault(address);
    }

    /// <summary>Labeler for a nation's mapped fields (FIFA points, youth rating, attendances, importance...) at the addresses they resolve to now.</summary>
    public static Func<ulong, int, ChangeLabel?> ForNation(Fm27Editor.Core.Nations.NationFieldAccess access, ulong nation)
    {
        var byAddress = new Dictionary<ulong, ChangeLabel>();
        foreach (var (field, offset) in access.Fields)
        {
            if (access.AddressOf(nation, field) is not { } at) continue;
            var label = Label(PlayerChangeLabels.Words(field.ToString()), offset);
            byAddress[at] = label;
            if (field == Fm27Editor.Core.Nations.NationField.Importance) byAddress[at + 1] = label;
        }
        return (address, _) => byAddress.TryGetValue(address, out var l) ? l : null;
    }

    /// <summary>Labeler for the competition object at <paramref name="competition"/> (reputation, colours, winter ball, substitution rules).</summary>
    public static Func<ulong, int, ChangeLabel?> ForCompetition(OffsetProfile profile, ulong competition)
    {
        var map = new Dictionary<int, ChangeLabel>();
        if (profile.Competition is { } c)
        {
            void Add(int? offset, string name) { if (offset is { } o) map[o] = new(name); }
            Add(c.Reputation, "Reputation");
            Add(c.ReputationMirror, "Reputation");
            Add(c.Foreground, "Foreground Colour");
            Add(c.Background, "Background Colour");
            Add(c.Trim, "Trim Colour");
            Add(c.WinterBallColour, "Winter Ball Colour");
            Add(c.WinterBallStart, "Winter Ball Start");
            Add(c.WinterBallEnd, "Winter Ball End");
            Add(c.SubstitutionRules, "Substitution Rules");
        }
        return (address, _) => address >= competition && address - competition < 0x1B0 && map.TryGetValue((int)(address - competition), out var l) ? l : null;
    }
}
