using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed partial class MassEditWindow
{
    /// <summary>A line for one person's value; null when the value would not change.</summary>
    MassRow? PerPerson(string name, FieldInput row, MassEditMode mode, long before, Action<long> write)
    {
        try
        {
            long after = Target(row, mode, before);
            return after == before ? null : MassRow.Change(name, row.Label, before, after, () => { write(after); return 1; });
        }
        catch (ArgumentOutOfRangeException ex) { return MassRow.Refused(name, row.Label, ex.Message); }
    }

    MassPlan PlayerPlan()
    {
        var session = backend.Session;
        var players = PlayerTargets();
        var rows = new List<MassRow>();
        foreach (var row in planFields.Where(f => f.ModeIndex != ModeNone))
        {
            var mode = row.ModeIndex == ModeSet ? MassEditMode.Set : MassEditMode.Change;
            switch (row.Kind)
            {
                case Kind.PlayerAttribute:
                    rows.AddRange(Guarded(row.Label, () => PlayerMassEdit.Plan(players, (PlayerAttribute)row.Index, mode, (int)row.Number)
                        .Select(c => MassRow.Change(c.Player.DisplayName, row.Label, c.Before, c.After, () => { PlayerMassEdit.Apply(c, session.Players, session.Writer); return 1; }))));
                    break;
                case Kind.PlayerQuality:
                    rows.AddRange(Guarded(row.Label, () => PlayerQualityMassEdit.Plan(players, (PlayerQuality)row.Index, mode, (int)row.Number)
                        .Select(c => MassRow.Change(c.Player.DisplayName, row.Label, c.Before, c.After, () => { PlayerQualityMassEdit.Apply(c, session.Players, session.Writer); return 1; }))));
                    break;
                case Kind.PlayerPersonality:
                    rows.AddRange(players.Select(p => PerPerson(p.DisplayName, row, mode, p.PersonalityValue((PersonalityAttribute)row.Index),
                        after => session.Writer.SetPersonality(p, (PersonalityAttribute)row.Index, (int)after))).OfType<MassRow>());
                    break;
                case Kind.PlayerPosition:
                    rows.AddRange(players.Select(p => PerPerson(p.DisplayName, row, mode, p.Position((PlayerPosition)row.Index),
                        after => session.Writer.SetPosition(p, (PlayerPosition)row.Index, (int)after))).OfType<MassRow>());
                    break;
                case Kind.PlayerWage:
                    rows.AddRange(Guarded(row.Label, () => PlayerWageMassEdit.Plan(players, mode, (int)row.Number)
                        .Select(c => MassRow.Change(c.Player.DisplayName, row.Label, c.Before, c.After, () => { PlayerWageMassEdit.Apply(c, session.Players, session.Contracts); return 1; }))));
                    break;
                case Kind.PlayerField:
                {
                    var access = session.PlayerFields;
                    var field = (PlayerField)row.Index;
                    foreach (var p in players)
                    {
                        if (access.Read(p, field) is not { } before) continue;
                        var line = PerPerson(p.DisplayName, row, mode, before, after => access.Set(p, field, before, after));
                        if (line is { Problem: null }) { try { access.ValidateValue(field, long.Parse(line.After)); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { line = MassRow.Refused(p.DisplayName, row.Label, ex.Message); } }
                        if (line is not null) rows.Add(line);
                    }
                    break;
                }
                case Kind.PlayerScalar:
                    rows.AddRange(Guarded(row.Label, () => PlayerScalarMassEdit.Plan(players, session.PlayerFields, (PlayerField)row.Index, mode, (int)row.Number)
                        .Select(c => MassRow.Change(c.Player.DisplayName, row.Label, c.Before, c.After, () => { PlayerPresetBatch.Apply(c, session.Players, session.PlayerFields); return 1; }))));
                    break;
            }
        }
        AddPlayerActions(rows, players);
        return new MassPlan("Player", players.Count, rows);
    }
}
