using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class GamePassContractWageTests
{
    [Fact]
    public void Verified_wages_are_read_and_written_without_touching_other_contract_fields()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, player1 = 0x10000000, player2 = 0x10001000,
            contract1 = 0x20000000, contract2 = 0x20001000;
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
        // Game Pass RAM, compared with game golden: Cunha £200,000 and Lammens £60,000.
        Map(player1, contract1, 19334410, "F80562DDFA7F000030B80A3772020000003EA72D72020000003EA72D72020000400D03000B0001000000000000000000000000000000000032660000FFFFFFFFFFFFFFFFA300E907B500EE07A300E90701006C07020002000000505C140A0000");
        Map(player2, contract2, 18107436, "F80562DDFA7F000020CEE13672020000003EA72D72020000003EA72D7202000060EA00000B0001000000000000000000000000000000000008250000FFFFFFFFFFFFFFFFF400E907B500EE07F400E90701006C070F000F000000324814010000");
        var repo = new PlayerRepository(memory, profile, module);
        var first = repo.Reload(player1)!;
        var second = repo.Reload(player2)!;
        Assert.Equal(200000, first.Contract!.Wage);
        Assert.Equal(60000, second.Contract!.Wage);
        Assert.Equal(new DateOnly(2030, 6, 30), second.Contract.Expires);
        var writer = new ContractWriter(memory, profile, module);
        var before = memory.Read(contract2, 0x60);
        writer.SetWage(second, 60001);
        var after = memory.Read(contract2, 0x60);
        Assert.Equal(60001, BitConverter.ToInt32(after, 0x20));
        before[0x20..0x24].CopyTo(after, 0x20);
        Assert.Equal(before, after);
        Assert.True(memory.TryWrite(player1 + 0x294, BitConverter.GetBytes(19334411)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetWage(first, 200001));
        Assert.Equal(writes, memory.Writes);
    }
}
