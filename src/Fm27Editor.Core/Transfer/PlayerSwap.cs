using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Transfer;

/// <summary>Where a player currently plays: the club and the team his playing-team pointer leads to.</summary>
public sealed record SwapTarget(ulong PlayerAddress, ulong ClubAddress, ulong TeamAddress);

/// <summary>One direction of a swap, planned with <see cref="TransferService"/> for a zero fee.</summary>
public sealed record SwapLeg(PlayerRecord Player, ClubEntry Buyer, TeamEntry IntoTeam, TransferPlan Plan);

public sealed record SwapResult(SwapLeg First, SwapLeg Second);

/// <summary>
/// Swaps two players between their clubs: each goes to the club (and the team) the other one played for, for no fee.
/// The money effect is the one a zero-fee move already has on both sides — the wage bills move by the two wages — so
/// nothing new is written beyond what <see cref="TransferService"/> has verified in game. A player on loan, a free
/// agent and a pair at the same club are refused. Registered squads (UEFA/domestic) and loans are not touched, exactly
/// like a single move.
/// </summary>
public sealed class PlayerSwapService(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    ClubOffsets Club => profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");

    /// <summary>The club and team the player currently plays for. Throws when either pointer is not valid.</summary>
    public SwapTarget Target(PlayerRecord player)
    {
        if (!IsPlayer(player.Address)) throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        ulong team = memory.ReadUInt64(player.Address + (ulong)T.PlayerTeam);
        if (team == 0 || memory.ReadUInt64(team) != moduleBase + T.VtableRva)
            throw new StaleObjectException($"{player.DisplayName} has no valid playing team");
        ulong club = memory.ReadUInt64(team + (ulong)C.TeamClub);
        if (club == 0 || memory.ReadUInt64(club) != moduleBase + Club.ClubVtableRva)
            throw new StaleObjectException($"{player.DisplayName}'s playing team leads to no club");
        return new(player.Address, club, team);
    }

    /// <summary>
    /// One leg: <paramref name="player"/> moves into <paramref name="target"/>'s club and team for no fee. The player
    /// must have a contract and must not be on loan; the destination team must belong to the destination club.
    /// </summary>
    public SwapLeg Plan(PlayerRecord player, SwapTarget target, IReadOnlyList<ClubEntry> clubs)
    {
        if (player.Contract is null) throw new InvalidOperationException($"{player.DisplayName} has no contract (free agent)");
        if (target.PlayerAddress == player.Address) throw new ArgumentException("A player cannot be swapped with himself", nameof(target));
        GenderGuard.EnsureMalePlayer(memory, profile, player.Address);
        GenderGuard.EnsureMalePlayer(memory, profile, target.PlayerAddress);
        GenderGuard.EnsureMenClub(memory, profile, target.ClubAddress);
        if (new LoanEditor(memory, profile, moduleBase).Read(player) is not null)
            throw new InvalidOperationException($"{player.DisplayName} is on loan; recall him before swapping");
        if (Target(player).ClubAddress == target.ClubAddress)
            throw new InvalidOperationException($"{player.DisplayName} already plays for that club");
        var buyer = clubs.FirstOrDefault(c => c.Address == target.ClubAddress)
            ?? throw new InvalidOperationException("The destination club is not in the loaded list");
        var into = buyer.Teams.FirstOrDefault(t => t.Address == target.TeamAddress)
            ?? throw new InvalidOperationException("The destination team does not belong to the destination club");
        // The destination team is chosen explicitly, so a club whose first team was not detected (its squad order, or a
        // club the directory no longer lists) must not block the move: mark this team as the first one for the plan.
        if (buyer.FirstTeam is null)
            buyer = buyer with { Teams = [.. buyer.Teams.Select(t => t.Address == into.Address ? t with { IsFirstTeam = true } : t)] };
        return new(player, buyer, into, new TransferService(memory, profile, moduleBase).Plan(player, clubs, buyer, 0, into));
    }

    public void Apply(SwapLeg leg, Func<int, ulong>? allocate = null) =>
        new TransferService(memory, profile, moduleBase).Apply(leg.Plan, allocate);

    /// <summary>
    /// Swaps both players. The second leg is planned after the first one is applied, because the first move changes
    /// both clubs' squads, wage bills and the first player's team; <paramref name="reloadPlayers"/> and
    /// <paramref name="reloadClubs"/> must return fresh reads.
    /// </summary>
    public SwapResult Swap(PlayerRecord first, PlayerRecord second, Func<IReadOnlyList<PlayerRecord>> reloadPlayers,
        Func<IReadOnlyList<ClubEntry>> reloadClubs, Func<int, ulong>? allocate = null)
    {
        var clubsBefore = reloadClubs();
        var targetForFirst = Target(second);
        var targetForSecond = Target(first);
        if (targetForFirst.ClubAddress == targetForSecond.ClubAddress)
            throw new InvalidOperationException("Both players are at the same club");
        // The second leg still needs the first player's old club; a club with no players left is not listed by
        // ClubDirectory, so keep its entry and add it back for that leg (its finance and team addresses do not move).
        var destinationSecond = clubsBefore.FirstOrDefault(c => c.Address == targetForSecond.ClubAddress)
            ?? throw new InvalidOperationException("The first player's club is not in the loaded list");
        var leg1 = Plan(first, targetForFirst, clubsBefore);
        Apply(leg1, allocate);
        try
        {
            var players = reloadPlayers();
            var clubsAfter = reloadClubs().ToList();
            if (clubsAfter.All(c => c.Address != destinationSecond.Address)) clubsAfter.Add(destinationSecond);
            var secondAfter = players.FirstOrDefault(p => p.Address == second.Address)
                ?? throw new StaleObjectException("The second player disappeared from the player list");
            var leg2 = Plan(secondAfter, targetForSecond, clubsAfter);
            Apply(leg2, allocate);
            return new(leg1, leg2);
        }
        catch (Exception failure)
        {
            // The first player has already moved: put him back where he was, so a failed swap never leaves half of it done.
            try
            {
                var playersNow = reloadPlayers();
                var clubsNow = reloadClubs().ToList();
                if (clubsNow.All(c => c.Address != destinationSecond.Address)) clubsNow.Add(destinationSecond);
                var firstNow = playersNow.FirstOrDefault(p => p.Address == first.Address)
                    ?? throw new StaleObjectException("The first player disappeared from the player list");
                Apply(Plan(firstNow, targetForSecond with { PlayerAddress = second.Address }, clubsNow), allocate);
            }
            catch (Exception undoFailure)
            {
                throw new InvalidOperationException(
                    $"The swap failed ({failure.Message}) and moving {first.DisplayName} back also failed ({undoFailure.Message}); he is now at the other club", failure);
            }
            throw new InvalidOperationException($"The swap failed ({failure.Message}); {first.DisplayName} was moved back", failure);
        }
    }

    bool IsPlayer(ulong address) =>
        profile.PlayerVtableRvas.Any(r => memory.ReadUInt64(address) == moduleBase + r);
}
