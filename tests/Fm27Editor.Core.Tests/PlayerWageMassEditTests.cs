using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PlayerWageMassEditTests
{
    [Fact]
    public void Wage_batch_restores_each_contract_and_rejects_replaced_contract()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, player1 = 0x10000000, player2 = 0x10001000, contract1 = 0x20000000, contract2 = 0x20001000, other = 0x20002000;
        void Map(ulong player, ulong contract, int id, string bytes)
        {
            var p = memory.Map(player, 0x3C8);
            BitConverter.GetBytes(module + 0x4CD6318u).CopyTo(p, 0);
            BitConverter.GetBytes(id).CopyTo(p, 0x294);
            BitConverter.GetBytes(contract).CopyTo(p, 0x330);
            var c = memory.Map(contract, 0x60);
            Convert.FromHexString(bytes).CopyTo(c, 0);
            BitConverter.GetBytes(module + 0x4AC05F8u).CopyTo(c, 0);
        }
        // Game Pass bytes checked against the game: Cunha 200,000; Lammens 60,000.
        const string cunha = "F80562DDFA7F000030B80A3772020000003EA72D72020000003EA72D72020000400D03000B0001000000000000000000000000000000000032660000FFFFFFFFFFFFFFFFA300E907B500EE07A300E90701006C07020002000000505C140A0000";
        const string lammens = "F80562DDFA7F000020CEE13672020000003EA72D72020000003EA72D7202000060EA00000B0001000000000000000000000000000000000008250000FFFFFFFFFFFFFFFFF400E907B500EE07F400E90701006C070F000F000000324814010000";
        Map(player1, contract1, 19334410, cunha); Map(player2, contract2, 18107436, lammens);
        var repo = new PlayerRepository(memory, profile, module);
        var writer = new ContractWriter(memory, profile, module);
        var plan = PlayerWageMassEdit.Plan([repo.Reload(player1)!, repo.Reload(player2)!], MassEditMode.Change, 100);
        Assert.Equal(new[] { (200000, 200100), (60000, 60100) }, plan.Select(c => (c.Before, c.After)));
        foreach (var change in plan) PlayerWageMassEdit.Apply(change, repo, writer);
        Assert.Equal(200100, BitConverter.ToInt32(memory.Read(contract1 + 0x20, 4)));
        Assert.Equal(60100, BitConverter.ToInt32(memory.Read(contract2 + 0x20, 4)));
        foreach (var change in plan.Reverse()) PlayerWageMassEdit.Undo(change, repo, writer);
        Assert.Equal(200000, BitConverter.ToInt32(memory.Read(contract1 + 0x20, 4)));
        Assert.Equal(60000, BitConverter.ToInt32(memory.Read(contract2 + 0x20, 4)));
        var replacement = memory.Map(other, 0x60);
        Convert.FromHexString(cunha).CopyTo(replacement, 0);
        BitConverter.GetBytes(module + 0x4AC05F8u).CopyTo(replacement, 0);
        Assert.True(memory.TryWrite(player1 + 0x330, BitConverter.GetBytes(other)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => PlayerWageMassEdit.Apply(plan[0], repo, writer));
        Assert.Equal(writes, memory.Writes);
    }
}
