namespace Fm27Editor.Core.Players;

public enum AttributeGroup { Technical, Mental, Physical, Goalkeeping, Hidden }

/// <summary>Player attributes stored on the game's 1-100 scale and shown as 1-20.</summary>
public enum PlayerAttribute
{
    // Technical
    Corners, Crossing, Dribbling, Finishing, FirstTouch, FreeKicks, Heading, LongShots,
    LongThrows, Marking, Passing, PenaltyTaking, Tackling, Technique,
    // Mental
    Aggression, Anticipation, Bravery, Composure, Concentration, Decisions, Determination,
    Flair, Leadership, OffTheBall, Positioning, Teamwork, Vision, WorkRate,
    // Physical
    Acceleration, Agility, Balance, JumpingReach, NaturalFitness, Pace, Stamina, Strength,
    LeftFoot, RightFoot,
    // Goalkeeping
    AerialReach, CommandOfArea, Communication, Eccentricity, Handling, Kicking, OneOnOnes,
    Reflexes, RushingOut, TendencyToPunch, Throwing,
    // Hidden
    Consistency, Dirtiness, ImportantMatches, InjuryProneness, Versatility,
}

public enum PlayerPosition
{
    Goalkeeper, DefenderLeft, DefenderCentral, DefenderRight, DefensiveMidfielder,
    MidfielderLeft, MidfielderCentral, MidfielderRight, AttackingMidLeft, AttackingMidCentral,
    AttackingMidRight, Striker, WingBackLeft, WingBackRight,
}

public enum PersonalityAttribute
{
    Adaptability, Ambition, Loyalty, Pressure, Professionalism, Sportsmanship, Temperament, Controversy,
}

public static class PlayerAttributeInfo
{
    public static AttributeGroup GroupOf(PlayerAttribute a) => a switch
    {
        <= PlayerAttribute.Technique => AttributeGroup.Technical,
        <= PlayerAttribute.WorkRate => AttributeGroup.Mental,
        <= PlayerAttribute.RightFoot => AttributeGroup.Physical,
        <= PlayerAttribute.Throwing => AttributeGroup.Goalkeeping,
        _ => AttributeGroup.Hidden,
    };
}
