using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed partial class MassEditWindow
{
    void AddContractActions(List<MassRow> rows, IReadOnlyList<PlayerRecord> players, string everyone)
    {
        var session = backend.Session;
        if (planContractStatus > 0 && session.ReadGameDate() is { } today)
        {
            int choice = planContractStatus;
            string after = choice switch { 1 => "Expired", 2 => "Expiring in 6 Months", _ => "Expiring in 1 Year" };
            rows.Add(MassRow.Action(everyone, "Contract", after, () =>
            {
                int count = 0;
                foreach (var player in players)
                {
                    if (session.Contracts.Read(player) is not { } details) continue;
                    if (choice == 1) session.Contracts.MakeExpire(player, today);
                    else session.Contracts.SetExpiry(player, details, today.AddMonths(choice == 2 ? 6 : 12));
                    count++;
                }
                return count;
            }));
        }
        if (planContractType > 0)
        {
            var type = planContractType switch
            {
                1 => ContractType.PartTime, 2 => ContractType.FullTime, 3 => ContractType.Amateur, 4 => ContractType.Youth, _ => ContractType.NonContract,
            };
            rows.Add(MassRow.Action(everyone, "Contract Type", type.ToString(), () =>
            {
                int count = 0;
                foreach (var player in players)
                {
                    var details = session.Contracts.Read(player);
                    if (details?.Type is not { } before || before == type) continue;
                    session.Contracts.SetType(player, details, type);
                    count++;
                }
                return count;
            }));
        }
        if (planClauseType > 0)
        {
            short code = ReleaseClauses[planClauseType - 1];
            int fee = planClauseFee;
            bool remove = planRemoveClause;
            rows.Add(MassRow.Action(everyone, ContractLabels.Term(code), remove ? "Remove" : $"Set to {fee:N0}", () =>
            {
                int count = 0;
                foreach (var player in players)
                {
                    var details = session.Contracts.Read(player);
                    if (details is null) continue;
                    var term = details.Terms.FirstOrDefault(t => t.Type == code);
                    if (remove)
                    {
                        if (term is null) continue;
                        session.Contracts.RemoveTerm(player, details, term.Index);
                    }
                    else if (term is null) session.Contracts.AddTerm(player, details, code, fee, -1, session.AllocateGameBlock);
                    else if (term.Value != fee) session.Contracts.SetTerm(player, details, term.Index, fee, term.Parameter);
                    else continue;
                    count++;
                }
                return count;
            }));
        }
    }
}
