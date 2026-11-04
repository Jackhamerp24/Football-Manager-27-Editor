using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>
/// FMRTE's names for relationship rows. Person reasons follow FMRTE 26's dropdown order; FMRTE 27 confirmed 2 Sibling,
/// 5 Teammate, 6 Manager and 9 Idol on Mbappé. Team reasons seen: 1 Supporter (Real Madrid), 2 Player (Monaco), 3 Manager (written by FMRTE).
/// </summary>
public static class RelationshipLabels
{
    static readonly string[] PersonReasons =
        ["None", "Parent", "Sibling", "Child", "Relationship", "Teammate", "Manager", "Player", "Backroom Staff", "Idol", "Friend", "Youth Staff"];

    /// <summary>FMRTE 27 Likes Team dropdown; writing Manager there stored 3 (Kane → Tottenham, 2/11/2026).</summary>
    static readonly string[] TeamReasons = ["None", "Supporter", "Player", "Manager", "Backroom Staff"];

    public static IReadOnlyList<string> Reasons(RelationshipKind kind) => kind == RelationshipKind.LikesTeam ? TeamReasons : PersonReasons;

    public static string KindName(RelationshipKind kind) => kind switch
    {
        RelationshipKind.LikesPerson => "Likes Person",
        RelationshipKind.DislikesPerson => "Dislikes Person",
        RelationshipKind.OtherNationality => "Other Nationality",
        _ => "Likes Team",
    };

    public static string ReasonName(RelationshipKind kind, int reason) => kind switch
    {
        RelationshipKind.LikesTeam => reason >= 0 && reason < TeamReasons.Length ? TeamReasons[reason] : $"Code {reason}",
        _ => reason >= 0 && reason < PersonReasons.Length ? PersonReasons[reason] : $"Code {reason}",
    };
}
