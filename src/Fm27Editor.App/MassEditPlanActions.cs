using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed partial class MassEditWindow
{
    /// <summary>The checkbox and dropdown actions of the Player scope (injuries, presets, preferred moves, contract); each becomes one line standing for all targets.</summary>
    void AddPlayerActions(List<MassRow> rows, IReadOnlyList<PlayerRecord> players)
    {
        var session = backend.Session;
        string everyone = $"{players.Count:N0} players";
        if (planInjuryClear)
            rows.Add(MassRow.Action(everyone, "Injuries", "Remove every injury", () =>
            {
                int count = 0;
                foreach (var player in players) foreach (var injury in session.ReadInjuries(player)) { session.RemoveInjury(player, injury); count++; }
                return count;
            }));
        if (planInjuryType is { } injuryTypeChoice)
            rows.Add(MassRow.Action(everyone, "Injuries", $"Add {injuryTypeChoice.Name}", () =>
            {
                int count = 0;
                foreach (var player in players)
                {
                    if (session.InjuryTemplate(player, players) is not { } template) continue;
                    session.AddInjury(player, template, injuryTypeChoice, InjuryTreatment.None);
                    count++;
                }
                return count;
            }));
        if (planPresetAll)
            rows.Add(MassRow.Action(everyone, "Preset", "All-in-one", () => { foreach (var player in players) session.AllInOne(player); return players.Count; }));
        if (planPresetTu)
            rows.Add(MassRow.Action(everyone, "Preset", "Tactical Understanding", () => { foreach (var player in players) session.TacticalUnderstanding(player); return players.Count; }));
        if (planMoveStates.Any(s => s != 0))
        {
            ulong on = 0, off = 0;
            for (int i = 0; i < planMoveStates.Length; i++)
            {
                if (planMoveStates[i] == 2) on |= 1UL << i;
                else if (planMoveStates[i] == 1) off |= 1UL << i;
            }
            rows.Add(MassRow.Action(everyone, "Preferred Moves", $"{System.Numerics.BitOperations.PopCount(on):N0} on, {System.Numerics.BitOperations.PopCount(off):N0} off", () =>
            {
                var moves = new PreferredMovesAccess(session.Memory, session.Profile, session.ModuleBase);
                int count = 0;
                foreach (var player in players)
                {
                    if (moves.Read(player) is not { } before) continue;
                    ulong next = ((before & PreferredMovesAccess.Editable) | on) & ~off;
                    if (next == (before & PreferredMovesAccess.Editable)) continue;
                    moves.Set(player, before, next);
                    count++;
                }
                return count;
            }));
        }
        if (planMatchCaToPa)
            rows.AddRange(players.Select(player => MassRow.Change(player.DisplayName, "Current Ability", player.CurrentAbility, player.PotentialAbility,
                () => { session.Writer.SetCurrentAbility(player, player.PotentialAbility); return 1; })));
        AddContractActions(rows, players, everyone);
    }
}
