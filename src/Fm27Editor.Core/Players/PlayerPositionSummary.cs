namespace Fm27Editor.Core.Players;

/// <summary>Compact position labels for ratings of 16 or above.</summary>
public static class PlayerPositionSummary
{
    public static string Format(IReadOnlyList<byte> positions)
    {
        bool Has(PlayerPosition p) => (int)p < positions.Count && positions[(int)p] >= 16;
        bool goalkeeper = Has(PlayerPosition.Goalkeeper);
        var roles = new List<string>();
        bool right = false, left = false, central = false;
        void Add(string label, PlayerPosition? r, PlayerPosition? l, PlayerPosition? c)
        {
            bool rr = r is { } rp && Has(rp), ll = l is { } lp && Has(lp), cc = c is { } cp && Has(cp);
            if (!rr && !ll && !cc) return;
            roles.Add(label); right |= rr; left |= ll; central |= cc;
        }
        Add("D", PlayerPosition.DefenderRight, PlayerPosition.DefenderLeft, PlayerPosition.DefenderCentral);
        Add("WB", PlayerPosition.WingBackRight, PlayerPosition.WingBackLeft, null);
        if (Has(PlayerPosition.DefensiveMidfielder)) { roles.Add("DM"); central = true; }
        Add("M", PlayerPosition.MidfielderRight, PlayerPosition.MidfielderLeft, PlayerPosition.MidfielderCentral);
        Add("AM", PlayerPosition.AttackingMidRight, PlayerPosition.AttackingMidLeft, PlayerPosition.AttackingMidCentral);
        if (Has(PlayerPosition.Striker)) roles.Add("ST");
        string result = string.Join("/", roles);
        string sides = (right ? "R" : "") + (left ? "L" : "") + (central ? "C" : "");
        if (sides.Length > 0 && roles.Any(r => r is not "ST" and not "DM")) result += " " + sides;
        else if (roles.Count == 1 && roles[0] == "DM") result += " C";
        return goalkeeper ? result.Length == 0 ? "GK" : "GK, " + result : result;
    }
}
