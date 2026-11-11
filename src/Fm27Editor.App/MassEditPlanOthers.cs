using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

internal sealed partial class MassEditWindow
{
    MassRow[] Guarded(string field, Func<IEnumerable<MassRow>> plan)
    {
        try { return plan().ToArray(); }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException or ArgumentException or StaleObjectException)
        { return [MassRow.Refused("(every target)", field, ex.Message)]; }
    }

    MassPlan CoachPlan()
    {
        var session = backend.Session;
        var staff = StaffTargets();
        var rows = new List<MassRow>();
        foreach (var row in planFields.Where(f => f.ModeIndex != ModeNone))
        {
            var mode = row.ModeIndex == ModeSet ? MassEditMode.Set : MassEditMode.Change;
            switch (row.Kind)
            {
                case Kind.StaffField:
                    rows.AddRange(Guarded(row.Label, () => StaffMassEdit.Plan(staff, (StaffMassField)row.Index, mode, (int)row.Number)
                        .Select(c => MassRow.Change(c.Staff.DisplayName, row.Label, c.Before, c.After, () => { StaffMassEdit.Apply(c, session.Staff, session.StaffWriter); return 1; }))));
                    break;
                case Kind.StaffAttribute:
                    rows.AddRange(Guarded(row.Label, () => StaffAttributeMassEdit.Plan(staff, (StaffAttribute)row.Index, mode, (int)row.Number, session.StaffWriter)
                        .Select(c => MassRow.Change(c.Staff.DisplayName, row.Label, c.Before, c.After, () => { StaffAttributeMassEdit.Apply(c, session.Staff, session.StaffWriter); return 1; }))));
                    break;
                case Kind.StaffPersonality:
                    rows.AddRange(Guarded(row.Label, () => StaffPersonalityMassEdit.Plan(staff, (StaffPersonality)row.Index, mode, (int)row.Number)
                        .Select(c => MassRow.Change(c.Staff.DisplayName, row.Label, c.Before, c.After, () => { StaffPersonalityMassEdit.Apply(c, session.Staff, session.StaffWriter); return 1; }))));
                    break;
                case Kind.StaffWage:
                    rows.AddRange(Guarded(row.Label, () => StaffWageMassEdit.Plan(staff, session.StaffContracts, mode, (int)row.Number).Changes
                        .Select(c => MassRow.Change(c.Staff.DisplayName, row.Label, c.Before, c.After, () => { StaffWageMassEdit.Apply(c, session.Staff, session.StaffContracts); return 1; }))));
                    break;
            }
        }
        if (planMatchCaToPa)
            rows.AddRange(staff.Select(person => MassRow.Change(person.DisplayName, "Current Ability", person.CurrentAbility, person.PotentialAbility,
                () => { session.StaffWriter.SetAbility(person, person.PotentialAbility, person.PotentialAbility); return 1; })));
        return new MassPlan("Coach", staff.Count, rows);
    }

    MassPlan ClubPlan()
    {
        var session = backend.Session;
        var clubs = ClubTargets();
        var rows = new List<MassRow>();
        foreach (var row in planFields.Where(f => f.ModeIndex != ModeNone))
        {
            var mode = row.ModeIndex == ModeSet ? MassEditMode.Set : MassEditMode.Change;
            rows.AddRange(Guarded(row.Label, () => ClubMassEdit.Plan(clubs, (ClubField)row.Index, mode, row.Number, row.Min, row.Max, session.Memory, session.Profile, session.ModuleBase)
                .Select(c => MassRow.Change(c.Club.DisplayName, row.Label, c.Before, c.After,
                    () => { ClubMassEdit.Apply(c, session.Memory, session.Profile, session.ModuleBase); return 1; }))));
        }
        return new MassPlan("Club", clubs.Count, rows);
    }

    MassPlan NationPlan()
    {
        var session = backend.Session;
        var nations = NationTargets();
        var rows = new List<MassRow>();
        foreach (var row in planFields.Where(f => f.ModeIndex != ModeNone))
        {
            var mode = row.ModeIndex == ModeSet ? MassEditMode.Set : MassEditMode.Change;
            rows.AddRange(Guarded(row.Label, () => NationMassEdit.Plan(nations, session.NationFields, (NationField)row.Index, mode, row.Number)
                .Select(c => MassRow.Change(c.Nation.DisplayName, row.Label, c.Before, c.After, () => { NationMassEdit.Apply(c, session.NationFields); return 1; }))));
        }
        return new MassPlan("Nation", nations.Count, rows);
    }
}
