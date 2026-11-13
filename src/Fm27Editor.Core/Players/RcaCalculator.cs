namespace Fm27Editor.Core.Players;

public sealed record PositionRca(PlayerPosition Position, int Rating, double Contribution, double UnweightedBase);

public sealed record PlayerRcaResult(int BestRca, PlayerPosition BestPosition, IReadOnlyList<PositionRca> Positions);

/// <summary>
/// Player Recommended Current Ability (RCA) across 14 positions and 48 raw (1-100) attributes,
/// matching FMCET Data.RCA. Does not depend on current in-game CA.
/// </summary>
public static class RcaCalculator
{
    public static readonly PlayerAttribute[] AttributeOrder =
    [
        // Goalkeeping (1..11)
        PlayerAttribute.AerialReach, PlayerAttribute.CommandOfArea, PlayerAttribute.Communication,
        PlayerAttribute.Eccentricity, PlayerAttribute.Handling, PlayerAttribute.Kicking,
        PlayerAttribute.OneOnOnes, PlayerAttribute.TendencyToPunch, PlayerAttribute.Reflexes,
        PlayerAttribute.RushingOut, PlayerAttribute.Throwing,
        // Technical (12..25)
        PlayerAttribute.Corners, PlayerAttribute.Crossing, PlayerAttribute.Dribbling,
        PlayerAttribute.Finishing, PlayerAttribute.FirstTouch, PlayerAttribute.FreeKicks,
        PlayerAttribute.Heading, PlayerAttribute.LongShots, PlayerAttribute.LongThrows,
        PlayerAttribute.Marking, PlayerAttribute.Passing, PlayerAttribute.PenaltyTaking,
        PlayerAttribute.Tackling, PlayerAttribute.Technique,
        // Mental (26..39)
        PlayerAttribute.Aggression, PlayerAttribute.Anticipation, PlayerAttribute.Bravery,
        PlayerAttribute.Composure, PlayerAttribute.Concentration, PlayerAttribute.Decisions,
        PlayerAttribute.Determination, PlayerAttribute.Flair, PlayerAttribute.Leadership,
        PlayerAttribute.OffTheBall, PlayerAttribute.Positioning, PlayerAttribute.Teamwork,
        PlayerAttribute.Vision, PlayerAttribute.WorkRate,
        // Physical (40..47)
        PlayerAttribute.Acceleration, PlayerAttribute.Agility, PlayerAttribute.Balance,
        PlayerAttribute.JumpingReach, PlayerAttribute.NaturalFitness, PlayerAttribute.Pace,
        PlayerAttribute.Stamina, PlayerAttribute.Strength,
    ];

    static readonly IReadOnlyDictionary<PlayerPosition, double[]> Weights = new Dictionary<PlayerPosition, double[]>
    {
        [PlayerPosition.Goalkeeper] = [6, 6, 5, 0, 8, 5, 4, 0, 8, 0, 3, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 3, 0, 0, 1, 0, 3, 6, 2, 6, 10, 0, 0, 2, 0, 5, 2, 1, 1, 6, 8, 2, 1, 0, 3, 1, 4, 3],
        [PlayerPosition.DefenderRight] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 1, 1, 3, 1, 2, 1, 1, 3, 2, 1, 4, 2, 0, 3, 2, 2, 4, 7, 0, 0, 1, 1, 4, 2, 2, 2, 7, 6, 2, 2, 0, 5, 6, 4, 4],
        [PlayerPosition.DefenderLeft] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 1, 1, 3, 1, 2, 1, 1, 3, 2, 1, 4, 2, 0, 3, 2, 2, 4, 7, 0, 0, 1, 1, 4, 2, 2, 2, 7, 6, 2, 2, 0, 5, 6, 4, 4],
        [PlayerPosition.DefenderCentral] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 1, 5, 1, 1, 8, 2, 1, 5, 1, 0, 5, 2, 2, 4, 10, 0, 0, 2, 1, 8, 1, 1, 2, 6, 6, 2, 6, 0, 5, 3, 6, 4.5],
        [PlayerPosition.WingBackRight] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3, 2, 1, 3, 1, 1, 1, 1, 2, 3, 1, 3, 3, 0, 3, 1, 2, 3, 5, 0, 0, 1, 2, 3, 2, 2, 2, 8, 5, 2, 1, 0, 6, 7, 4, 4],
        [PlayerPosition.WingBackLeft] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3, 2, 1, 3, 1, 1, 1, 1, 2, 3, 1, 3, 3, 0, 3, 1, 2, 3, 5, 0, 0, 1, 2, 3, 2, 2, 2, 8, 5, 2, 1, 0, 6, 7, 4, 4],
        [PlayerPosition.DefensiveMidfielder] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 4, 1, 1, 3, 1, 3, 4, 1, 7, 3, 0, 5, 1, 2, 3, 8, 0, 0, 1, 1, 5, 2, 4, 4, 6, 6, 2, 1, 0, 4, 4, 5, 5],
        [PlayerPosition.MidfielderRight] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 5, 3, 2, 4, 1, 1, 2, 1, 1, 3, 1, 2, 4, 0, 3, 1, 3, 2, 5, 0, 0, 1, 2, 1, 2, 3, 3, 8, 6, 2, 1, 0, 6, 5, 3, 5],
        [PlayerPosition.MidfielderLeft] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 5, 3, 2, 4, 1, 1, 2, 1, 1, 3, 1, 2, 4, 0, 3, 1, 3, 2, 5, 0, 0, 1, 2, 1, 2, 3, 3, 8, 6, 2, 1, 0, 6, 5, 3, 5],
        [PlayerPosition.MidfielderCentral] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 6, 1, 1, 3, 1, 3, 6, 1, 3, 4, 0, 3, 1, 3, 2, 7, 0, 0, 1, 3, 3, 2, 6, 3, 6, 6, 2, 1, 0, 5, 6, 4, 6],
        [PlayerPosition.AttackingMidRight] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 5, 5, 2, 5, 1, 1, 2, 1, 1, 2, 1, 2, 4, 0, 3, 1, 3, 2, 5, 0, 0, 1, 2, 1, 2, 3, 3, 10, 6, 2, 1, 0, 10, 7, 3, 5.5],
        [PlayerPosition.AttackingMidLeft] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 5, 5, 2, 5, 1, 1, 2, 1, 1, 2, 1, 2, 4, 0, 3, 1, 3, 2, 5, 0, 0, 1, 2, 1, 2, 3, 3, 10, 6, 2, 1, 0, 10, 7, 3, 5.5],
        [PlayerPosition.AttackingMidCentral] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 3, 3, 5, 1, 1, 3, 1, 1, 4, 1, 2, 5, 0, 3, 1, 3, 2, 6, 0, 0, 1, 3, 2, 2, 6, 3, 9, 6, 2, 1, 0, 7, 6, 3, 7],
        [PlayerPosition.Striker] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 5, 8, 6, 1, 6, 2, 1, 1, 2, 1, 1, 4, 0, 5, 1, 6, 2, 5, 0, 0, 1, 6, 2, 1, 2, 2, 10, 6, 2, 5, 0, 7, 6, 6, 7.5],
    };

    static readonly IReadOnlyDictionary<PlayerPosition, double> WeightSums =
        Weights.ToDictionary(kv => kv.Key, kv => kv.Value.Sum());

    public static int Calculate(PlayerRecord player) => Analyze(player).BestRca;

    public static PlayerRcaResult Analyze(PlayerRecord player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Span<double> raw48 = stackalloc double[48];
        for (int i = 0; i < 47; i++)
            raw48[i] = player.Raw(AttributeOrder[i]);
        raw48[47] = Math.Min(player.Raw(PlayerAttribute.LeftFoot), player.Raw(PlayerAttribute.RightFoot));

        var nat20 = new List<double>();
        var accomplished18 = new List<double>();
        var accomplished15 = new List<double>();
        var competent12 = new List<double>();
        var breakdown = new List<PositionRca>();

        PlayerPosition bestPos = PlayerPosition.Goalkeeper;
        double bestScore = double.NegativeInfinity;

        foreach (var (pos, weights) in Weights)
        {
            double dot = 0;
            for (int i = 0; i < 48; i++)
                dot += raw48[i] * weights[i];
            double unweighted = ((dot / WeightSums[pos]) - 30.0) * 4.0 - 1.0;
            int rating = player.Position(pos);

            double contrib = 0;
            if (rating == 20) { contrib = unweighted * 1.0; nat20.Add(contrib); }
            else if (rating is >= 18 and <= 19) { contrib = unweighted * 0.025; accomplished18.Add(contrib); }
            else if (rating is >= 15 and <= 17) { contrib = unweighted * 0.00625; accomplished15.Add(contrib); }
            else if (rating is >= 12 and <= 14) { contrib = unweighted * 0.0015625; competent12.Add(contrib); }

            if (rating >= 12)
                breakdown.Add(new PositionRca(pos, rating, contrib, unweighted));

            if (rating >= 15 && unweighted > bestScore)
            {
                bestScore = unweighted;
                bestPos = pos;
            }
        }

        if (bestScore == double.NegativeInfinity && breakdown.Count > 0)
            bestPos = breakdown.OrderByDescending(b => b.UnweightedBase).First().Position;

        double total = 0;
        if (nat20.Count > 0) total += nat20.Average();
        if (accomplished18.Count > 0) total += accomplished18.Sum();
        if (accomplished15.Count > 0) total += accomplished15.Sum();
        if (competent12.Count > 0) total += competent12.Sum();

        int rca = Math.Clamp((int)Math.Floor(total + 0.5), 1, 200);
        breakdown.Sort(static (a, b) => b.Rating != a.Rating ? b.Rating.CompareTo(a.Rating) : b.UnweightedBase.CompareTo(a.UnweightedBase));
        return new PlayerRcaResult(rca, bestPos, breakdown);
    }
}
