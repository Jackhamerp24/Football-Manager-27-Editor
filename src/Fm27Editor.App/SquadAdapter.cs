using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public static class SquadAdapter
{
    public static IReadOnlyList<PlayerRecord> ReadSquad(GameSession session, ulong teamAddress, IReadOnlyList<PlayerRecord>? allPlayers = null) =>
        session.ReadSquad(teamAddress);

    public static IReadOnlyDictionary<PlayerField, long> ReadAllPlayerFields(GameSession session, PlayerRecord player) =>
        session.PlayerFields.ReadAll(player);

    public static int? ReadSquadNumber(GameSession session, PlayerRecord player)
    {
        if (player.Contract is not { } c) return null;
        if (session.Profile.Contract?.SquadNumber is not { } off) return null;
        Span<byte> buf = stackalloc byte[1];
        if (session.Memory.TryRead(c.Address + (ulong)off, buf))
        {
            int num = (sbyte)buf[0];
            return num > 0 ? num : null;
        }
        return null;
    }

    public static void RenewForYears(GameSession session, PlayerRecord player, int years) =>
        session.RenewContract(player, years);

    public static void MakeExpire(GameSession session, PlayerRecord player) =>
        session.ExpireContract(player);

    public static void ShiftExpiry(GameSession session, PlayerRecord player, int years) =>
        session.Contracts.ShiftExpiry(player, years);
}
