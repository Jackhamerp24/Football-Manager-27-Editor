using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// The composed player presets of FMRTE 27, in the order the JSON presets run them. Each step is guarded on its own and a
/// value already at its target is not written.
/// </summary>
public sealed class PlayerPresetRunner(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>
    /// All-in-one: Heal, Inspire, every match ban removed, Remove Int. Withdrawal, tactical familiarity to its maximum. FMRTE's preset also ends
    /// the unhappiness motives and improves team cohesion; both are steps of <see cref="PlayerInspirer"/>.
    /// </summary>
    public void AllInOne(PlayerRecord player, DateOnly gameDate)
    {
        new PlayerHealer(memory, profile, moduleBase).Heal(player);
        new PlayerInspirer(memory, profile, moduleBase).Inspire(player, gameDate);
        new BanWriter(memory, profile, moduleBase).RemoveMatchBans(player);
        new RelationshipWriter(memory, profile, moduleBase).RemoveInternationalWithdrawal(player);
        MaximiseTactics(player);
    }

    /// <summary>
    /// FMRTE 27's Tactical Understanding step: every slider of the player's tactical understanding to 100% (<see cref="PlayerTacticalUnderstanding"/>).
    /// Profiles without those offsets keep the older single familiarity byte.
    /// </summary>
    void MaximiseTactics(PlayerRecord player)
    {
        var tactics = new PlayerTacticalUnderstanding(memory, profile, moduleBase);
        if (tactics.Available) { tactics.Maximise(player); return; }
        var familiarityAccess = new PlayerFamiliarityAccess(memory, profile, moduleBase);
        if (familiarityAccess.Read(player.Address) is { } familiarity && familiarity.Tactical != PlayerFamiliarity.MaxTactical)
            familiarityAccess.SetTactical(familiarity, PlayerFamiliarity.MaxTactical);
    }

    /// <summary>Tactical Understanding: every tactical understanding value to 100% and maximum team cohesion.</summary>
    public void TacticalUnderstanding(PlayerRecord player)
    {
        MaximiseTactics(player);
        ulong club = PlayingClub.Of(memory, profile, moduleBase, player);
        if (club != 0)
            new TeamCohesionWriter(memory, profile, moduleBase).Set(player, club, TeamCohesionWriter.Improve);
    }
}
