using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>What each tool does, in plain words, shown when the mouse rests on its button.</summary>
    static readonly Dictionary<string, string> ToolTips = new()
    {
        ["Renew for 4 Years"] = "Extends the contract so it runs four more years.",
        ["Make it Expire"] = "Ends the contract now, so the player can leave on a free transfer.",
        ["−1 Year"] = "Moves the contract end one year earlier.",
        ["+1 Year"] = "Moves the contract end one year later.",
        ["Grant work permit"] = "Gives every ticked player a work permit for the country the club plays in. Players who already have one are left alone.",
        ["Release Player"] = "Releases the player from his club; he becomes a free agent.",
        ["Undo last release"] = "Puts the last released player back at his club.",
        ["Set captain"] = "Makes this player the team captain.",
        ["Set vice captain"] = "Makes this player the vice captain.",
        ["Undo captain change"] = "Puts the previous captain back.",
        ["Heal"] = "Removes injuries and brings fitness and match sharpness back to full.",
        ["Inspire"] = "Makes the player as happy and motivated as possible.",
        ["Destroy"] = "The opposite of Inspire: unhappy, tired and out of form.",
        ["No Bans"] = "Removes match suspensions.",
        ["Freeze"] = "Keeps the player's attributes from changing while you play.",
        ["Unfreeze"] = "Lets the player's attributes change again.",
        ["Remove Int. Withdrawal"] = "Lets a player who quit international football be called up again.",
        ["Register for UEFA"] = "Registers the player for the club's European competitions.",
        ["Register for Domestic"] = "Registers the player for the club's league and cups.",
        ["Trained at club"] = "Counts the player as trained at his current club (home-grown at club).",
        ["Trained at nation"] = "Counts the player as trained in the club's country (home-grown in nation).",
        ["Team Cohesion"] = "Makes the player gel fully with his team-mates.",
        ["Tactical Understanding"] = "Makes the player fully used to the team's tactics.",
        ["All-in-one"] = "Heal, Inspire, No Bans and Tactical Understanding at once.",
        ["Prevent retirement"] = "Cancels a planned retirement so the player keeps playing.",
        ["Golden Generation"] = "Turns every youth and reserve player of the club into a top prospect: high potential, good ability, very professional and ambitious.",
        ["Golden Batch"] = "Only for the newly arrived youth intake: take a snapshot before intake day, then after it give just the new players a high potential. You see the result before anything is written.",
        ["Set speed"] = "Makes the player improve faster from training while the game runs. Normal speed turns it off.",
    };

    /// <summary>
    /// "Grant work permit" on a squad: a permit from the club's own country, for this year, to every ticked player. Players
    /// who already hold one are skipped. Runs on the UI thread (the edit history updates the window), under a second a squad.
    /// </summary>
    void GrantClubNationPermits(ulong club, Func<IReadOnlyList<PlayerRecord>>? getSelectedPlayers, Action<string>? onStatus, Action? onApplied)
    {
        var targets = getSelectedPlayers?.Invoke() ?? [];
        if (targets.Count == 0) { onStatus?.Invoke("No players selected"); return; }
        try
        {
            var session = backend.Session;
            ulong nation = session.NationOfClub(club);
            string nationName = NationCatalogueForPicker().FirstOrDefault(n => n.Address == nation)?.DisplayName ?? "the club's country";
            int year = session.ReadGameDate()?.Year ?? throw new InvalidOperationException("The in-game date is not readable");
            var skips = new List<WorkPermitBatchSkip>();
            int granted = 0;
            backend.Edits.Execute("Player", nationName, "Work permit", () =>
                granted = session.BatchGrantWorkPermits(targets, nation, year, a => a == nation ? nationName : null, skips.Add));
            onApplied?.Invoke();
            int already = skips.Count(s => s.Reason.StartsWith("already holds", StringComparison.Ordinal));
            var other = skips.Where(s => !s.Reason.StartsWith("already holds", StringComparison.Ordinal)).ToList();
            onStatus?.Invoke($"Work permit ({nationName}): {granted} granted" +
                (already > 0 ? $", {already} already had one" : "") +
                (other.Count > 0 ? $", {other.Count} failed ({string.Join("; ", other.Take(2).Select(s => $"{s.PlayerName}: {s.Reason}"))})" : "") +
                ". Continue in FM to see updates.");
        }
        catch (Exception ex) { onStatus?.Invoke($"Work permit: {UserMessage.For(ex)}"); }
    }
}
