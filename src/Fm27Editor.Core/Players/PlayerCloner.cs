using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>FMRTE 27's Clone Center "What to copy" groups.</summary>
[Flags]
public enum CloneParts
{
    None = 0,
    /// <summary>Born, City Of Birth, Nation, Height, Ethnicity, Skin/Hair Colour, Hair Length, Asking Price, Morale, Days In
    /// Country, Retiring. FMRTE also copies the names; the editor cannot change names yet.</summary>
    PersonalDetails = 1, Ability = 2, Reputation = 4, International = 8, Goalkeeper = 16, Technical = 32, Mental = 64,
    Physical = 128, Personality = 256, Hidden = 512, Positions = 1024, PreferredMoves = 2048,
    All = 4095,
}

/// <summary>
/// Copies the chosen groups from one player onto another, field by field through the guarded writers (each checks the
/// target still holds what it held when read). Fields already equal are skipped. Returns how many fields were written.
/// </summary>
public sealed class PlayerCloner(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    static readonly PlayerField[] Personal =
    [
        PlayerField.Ethnicity, PlayerField.SkinColour, PlayerField.HairColour, PlayerField.HairLength, PlayerField.AskingPrice,
        PlayerField.Morale, PlayerField.ArrivedInCountry, PlayerField.RetiringAfterSpellAtCurrentClub,
    ];
    static readonly PlayerField[] International = [PlayerField.Caps, PlayerField.InternationalGoals, PlayerField.SubCaps, PlayerField.SubGoals];

    public int Clone(PlayerRecord source, PlayerRecord target, CloneParts parts)
    {
        if (source.Address == target.Address) throw new InvalidOperationException("Pick another player to replace");
        var w = new PlayerWriter(memory, profile, moduleBase);
        var fields = new PlayerFieldAccess(memory, profile, moduleBase);
        int n = 0;

        if (parts.HasFlag(CloneParts.PersonalDetails))
        {
            if (source.DateOfBirth is { } dob && target.DateOfBirth != dob) { w.SetDateOfBirth(target, dob); n++; }
            if (source.Height != target.Height) { w.SetHeight(target, source.Height); n++; }
            n += CopyFields(fields, source, target, Personal);
            var origin = new PlayerOriginWriter(memory, profile, moduleBase);
            var cities = new CityRepository(memory, profile, moduleBase);
            if (profile.Cities is not null && cities.CityOf(source) is { } city && cities.CityPointer(target) != city.Address)
            { origin.SetCity(target, cities.CityPointer(target), city); n++; }
            if (source.Nationality is { } nation && target.Nationality is { } old && nation.Address != old.Address)
            { origin.SetNationality(target, old.Address, new Nations.NationRecord(nation.Address, null, nation.Name, nation.ShortName)); n++; }
        }
        if (parts.HasFlag(CloneParts.Ability))
        {
            if (source.CurrentAbility != target.CurrentAbility) { w.SetCurrentAbility(target, source.CurrentAbility); n++; }
            if (source.PotentialAbility != target.PotentialAbility) { w.SetPotentialAbility(target, source.PotentialAbility); n++; }
        }
        if (parts.HasFlag(CloneParts.Reputation))
            foreach (var (kind, s, t) in new[] { (Reputation.Home, source.HomeReputation, target.HomeReputation), (Reputation.Current, source.CurrentReputation, target.CurrentReputation), (Reputation.World, source.WorldReputation, target.WorldReputation) })
                if (s != t) { w.SetReputation(target, kind, s); n++; }
        if (parts.HasFlag(CloneParts.International)) n += CopyFields(fields, source, target, International);

        var groups = new List<AttributeGroup>();
        if (parts.HasFlag(CloneParts.Goalkeeper)) groups.Add(AttributeGroup.Goalkeeping);
        if (parts.HasFlag(CloneParts.Technical)) groups.Add(AttributeGroup.Technical);
        if (parts.HasFlag(CloneParts.Mental)) groups.Add(AttributeGroup.Mental);
        if (parts.HasFlag(CloneParts.Physical)) groups.Add(AttributeGroup.Physical);
        if (parts.HasFlag(CloneParts.Hidden)) groups.Add(AttributeGroup.Hidden);
        foreach (var a in Enum.GetValues<PlayerAttribute>().Where(a => groups.Contains(PlayerAttributeInfo.GroupOf(a))))
            if (source.Raw(a) != target.Raw(a)) { w.SetAttributeRaw(target, a, source.Raw(a)); n++; }

        if (parts.HasFlag(CloneParts.Personality))
            foreach (var p in Enum.GetValues<PersonalityAttribute>())
                if (source.PersonalityValue(p) != target.PersonalityValue(p)) { w.SetPersonality(target, p, source.PersonalityValue(p)); n++; }
        if (parts.HasFlag(CloneParts.Positions))
            foreach (var p in Enum.GetValues<PlayerPosition>())
                if (source.Position(p) != target.Position(p)) { w.SetPosition(target, p, source.Position(p)); n++; }
        if (parts.HasFlag(CloneParts.PreferredMoves) && profile.PreferredMoves is not null)
        {
            var moves = new PreferredMovesAccess(memory, profile, moduleBase);
            if (moves.Read(source) is { } s && moves.Read(target) is { } t && (s & PreferredMovesAccess.Editable) != (t & PreferredMovesAccess.Editable))
            { moves.Set(target, t, s & PreferredMovesAccess.Editable); n++; }
        }
        return n;
    }

    static int CopyFields(PlayerFieldAccess fields, PlayerRecord source, PlayerRecord target, IEnumerable<PlayerField> list)
    {
        int n = 0;
        foreach (var f in list.Where(fields.IsMapped))
            if (fields.Read(source, f) is { } s && fields.Read(target, f) is { } t && s != t) { fields.Set(target, f, t, s); n++; }
        return n;
    }
}
