namespace Fm27Editor.Core.Players;

public sealed record PersonalityClassification(int Id, string Name, bool IsNegative, bool IsRegenOnly = false);

/// <summary>
/// Sequential 39-personality classifier extracted from FMCET Data.Personality bytecode.
/// Later matching rules intentionally overwrite earlier ones (e.g. Determined -> Driven -> Perfectionist).
/// Negative personalities (Casual, Slack, Spineless, Temperamental, Unsporting) apply only to regens/newgens.
/// </summary>
public static class PersonalityClassifier
{
    public static IReadOnlyList<int> NegativePersonalityIds { get; } = [3, 31, 32, 35, 37];

    public static PersonalityClassification Classify(PlayerRecord player, bool? isRegenOverride = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        bool isRegen = isRegenOverride ?? (RegenIds.IsRegen(player) == true);
        int age = player.Age ?? 0;
        return Classify(
            isRegen,
            age,
            player.PersonalityValue(PersonalityAttribute.Ambition),
            player.PersonalityValue(PersonalityAttribute.Loyalty),
            player.PersonalityValue(PersonalityAttribute.Pressure),
            player.PersonalityValue(PersonalityAttribute.Professionalism),
            player.PersonalityValue(PersonalityAttribute.Sportsmanship),
            player.PersonalityValue(PersonalityAttribute.Temperament),
            player.Attribute(PlayerAttribute.Determination),
            player.Attribute(PlayerAttribute.Leadership));
    }

    public static PersonalityClassification Classify(
        bool isRegen,
        int age,
        int ambition,
        int loyalty,
        int pressure,
        int professionalism,
        int sportsmanship,
        int temperament,
        int determination,
        int leadership)
    {
        int id = 255;
        if (professionalism < 15 && determination < 15 && ambition < 15 && loyalty < 15 && sportsmanship < 15) id = 1; // Balanced
        if (17 < professionalism && determination < 15 && temperament < 10) id = 12; // Fairly Professional
        if (14 < professionalism && professionalism < 18 && determination < 15) id = 12; // Fairly Professional
        if (professionalism < 15 && determination < 15 && 5 < ambition && ambition < 15 && 14 < loyalty) id = 11; // Fairly Loyal
        if (professionalism < 15 && determination < 15 && 15 < ambition && 9 < loyalty) id = 9; // Fairly Ambitious
        if (professionalism < 15 && determination < 15 && ambition == 15) id = 9; // Fairly Ambitious
        if (isRegen && professionalism < 15 && 14 < determination && determination < 18 && 4 < sportsmanship && pressure < 17) id = 10; // Fairly Determined
        if (!isRegen && professionalism < 15 && 14 < determination && determination < 18 && pressure < 17) id = 10; // Fairly Determined
        if (professionalism < 15 && determination < 15 && ambition < 15 && loyalty < 15 && 14 < sportsmanship) id = 13; // Fairly Sporting
        if (isRegen && 4 < professionalism && determination < 10 && sportsmanship < 18 && pressure == 1) id = 32; // Spineless
        if (isRegen && 4 < professionalism && determination < 10 && sportsmanship < 18 && 1 < pressure && pressure < 4) id = 21; // Low Self Belief
        if (isRegen && 14 < professionalism && 14 < determination && 4 < sportsmanship && determination < 18 && pressure < 17) id = 30; // Resolute
        if (!isRegen && 14 < professionalism && 14 < determination && determination < 18 && pressure < 17) id = 30; // Resolute
        if (10 < professionalism && professionalism < 18 && determination < 18 && sportsmanship < 15 && 14 < pressure && 9 < temperament) id = 33; // Spirited
        if (professionalism < 11 && determination < 18 && sportsmanship < 15 && 14 < pressure && 9 < temperament) id = 17; // Jovial
        if (professionalism < 18 && determination < 18 && 14 < sportsmanship && 14 < pressure && 9 < temperament) id = 19; // Light-Hearted
        if (isRegen && 4 < professionalism && 1 < determination && determination < 6 && ambition < 10 && sportsmanship < 18) id = 20; // Low Determination
        if (isRegen && 14 < determination && determination < 18 && 4 < sportsmanship && 16 < pressure && pressure < 20) id = 29; // Resilient
        if (!isRegen && 14 < determination && determination < 18 && 16 < pressure && pressure < 20) id = 29; // Resilient
        if (isRegen && 14 < determination && determination < 18 && 4 < sportsmanship && pressure == 20) id = 16; // Iron Willed
        if (!isRegen && 14 < determination && determination < 18 && pressure == 20) id = 16; // Iron Willed
        if (isRegen && 5 < determination && determination < 18 && 5 < ambition && ambition < 8 && 17 < loyalty && loyalty < 20) id = 22; // Loyal
        if (!isRegen && 5 < determination && determination < 18 && ambition < 8 && 17 < loyalty && loyalty < 20) id = 22; // Loyal
        if (5 < determination && determination < 18 && 5 < ambition && ambition < 8 && loyalty == 20) id = 6; // Very Loyal
        if (professionalism < 18 && determination < 10 && sportsmanship == 20) id = 15; // Honest
        if (isRegen && 4 < professionalism && determination < 10 && 17 < sportsmanship && sportsmanship < 20) id = 34; // Sporting
        if (!isRegen && determination < 10 && 17 < sportsmanship && sportsmanship < 20) id = 34; // Sporting
        if (isRegen && 10 < determination && determination < 18 && sportsmanship == 1) id = 37; // Unsporting
        if (isRegen && 10 < determination && determination < 18 && 1 < sportsmanship && sportsmanship < 5) id = 28; // Realist
        if (isRegen && 4 < professionalism && determination == 1 && ambition < 20) id = 8; // Easily Discouraged
        if (isRegen && determination < 18 && ambition < 6 && 10 < loyalty) id = 36; // Unambitious
        if (isRegen && determination < 18 && 15 < ambition && ambition < 20 && 6 < loyalty && loyalty < 10) id = 0; // Ambitious
        if (!isRegen && determination < 18 && 15 < ambition && ambition < 20 && loyalty < 10) id = 0; // Ambitious
        if (17 < determination && ambition < 12) id = 5; // Determined
        if (17 < determination && 11 < ambition) id = 7; // Driven
        if (isRegen && determination < 18 && ambition == 20 && 6 < loyalty && loyalty < 10) id = 38; // Very Ambitious
        if (!isRegen && determination < 18 && ambition == 20 && loyalty < 10) id = 38; // Very Ambitious
        if (isRegen && determination < 18 && 15 < ambition && loyalty < 4) id = 23; // Mercenary
        if (isRegen && determination < 18 && 15 < ambition && 3 < loyalty && loyalty < 7) id = 14; // Fickle
        if (professionalism == 20 && 9 < temperament && 23 < age) id = 25; // Model Professional
        if (18 < leadership && 23 < age) id = 18; // Leader
        if (17 < sportsmanship && 17 < temperament && 18 < leadership && 23 < age) id = 4; // Charismatic Leader
        if (determination == 20 && leadership == 20 && 23 < age) id = 2; // Born Leader
        if (17 < professionalism && 9 < temperament && age < 24) id = 27; // Professional
        if (17 < professionalism && professionalism < 20 && 9 < temperament && 23 < age) id = 27; // Professional
        if (14 < professionalism && 13 < determination && 11 < ambition && 14 < loyalty && 14 < sportsmanship && 13 < pressure && 14 < temperament) id = 24; // Model Citizen
        if (13 < professionalism && 13 < determination && 13 < ambition && temperament < 10) id = 26; // Perfectionist
        if (isRegen && 1 < professionalism && professionalism < 5 && determination < 10 && 5 < temperament) id = 3; // Casual
        if (isRegen && professionalism == 1 && determination < 10 && 4 < temperament) id = 31; // Slack
        if (isRegen && professionalism < 11 && temperament < 5) id = 35; // Temperamental
        return Describe(id);
    }

    public static PersonalityClassification Describe(int id) => id switch
    {
        0 => new(0, "Ambitious", false),
        1 => new(1, "Balanced", false),
        2 => new(2, "Born Leader", false),
        3 => new(3, "Casual", true),
        4 => new(4, "Charismatic Leader", false),
        5 => new(5, "Determined", false),
        6 => new(6, "Very Loyal", false),
        7 => new(7, "Driven", false),
        8 => new(8, "Easily Discouraged", false),
        9 => new(9, "Fairly Ambitious", false),
        10 => new(10, "Fairly Determined", false),
        11 => new(11, "Fairly Loyal", false),
        12 => new(12, "Fairly Professional", false),
        13 => new(13, "Fairly Sporting", false),
        14 => new(14, "Fickle", false),
        15 => new(15, "Honest", false),
        16 => new(16, "Iron Willed", false),
        17 => new(17, "Jovial", false),
        18 => new(18, "Leader", false),
        19 => new(19, "Light-Hearted", false),
        20 => new(20, "Low Determination", false),
        21 => new(21, "Low Self Belief", false),
        22 => new(22, "Loyal", false),
        23 => new(23, "Mercenary", false),
        24 => new(24, "Model Citizen", false),
        25 => new(25, "Model Professional", false),
        26 => new(26, "Perfectionist", false),
        27 => new(27, "Professional", false),
        28 => new(28, "Realist", false),
        29 => new(29, "Resilient", false),
        30 => new(30, "Resolute", false),
        31 => new(31, "Slack", true),
        32 => new(32, "Spineless", true),
        33 => new(33, "Spirited", false),
        34 => new(34, "Sporting", false),
        35 => new(35, "Temperamental", true),
        36 => new(36, "Unambitious", false),
        37 => new(37, "Unsporting", true),
        38 => new(38, "Very Ambitious", false),
        255 => new(255, "Unclassified", false),
        _ => new(255, "Unclassified", false),
    };
}
