using Fm27Editor.App;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Tests;

namespace Fm27Editor.App.Tests;

public class PlayerAttributeFileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileContainsOnlyAttributesAndCanBeImported(bool csv)
    {
        var attributes = Enumerable.Repeat((byte)50, Enum.GetValues<PlayerAttribute>().Length).ToArray();
        attributes[(int)PlayerAttribute.Crossing] = 85;
        var personality = Enumerable.Repeat((byte)12, Enum.GetValues<PersonalityAttribute>().Length).ToArray();
        var player = new PlayerRecord { Address = 1, VtableRva = 2, CurrentAbility = 150, PotentialAbility = 170,
            HomeReputation = 1, CurrentReputation = 2, WorldReputation = 3, Height = 180,
            RawAttributes = attributes, AttributeScale = 5, Positions = new byte[Enum.GetValues<PlayerPosition>().Length], Personality = personality };
        var content = PlayerAttributeFile.Export(player, csv);
        var result = PlayerAttributeFile.Parse(content, csv);
        Assert.Equal(17, result["Crossing"]);
        Assert.Equal(12, result["Personality.Ambition"]);
        Assert.DoesNotContain("CA", result.Keys);
        Assert.DoesNotContain("Position", result.Keys);
        var unknown = csv ? content.Replace("Personality.Ambition,12", "Personality.Ambition,0") : content.Replace("\"Personality.Ambition\": 12", "\"Personality.Ambition\": 0");
        Assert.Equal(0, PlayerAttributeFile.Parse(unknown, csv)["Personality.Ambition"]);
        Assert.Throws<FormatException>(() => PlayerAttributeFile.Parse(csv ? content.Replace("Crossing,17", "Crossing,21") : content.Replace("\"Crossing\": 17", "\"Crossing\": 21"), csv));
    }

    [Fact]
    public void ImportStagesAttributesThenGuardedSaveKeepsAbility()
    {
        var attributes = Enumerable.Repeat((byte)50, Enum.GetValues<PlayerAttribute>().Length).ToArray();
        attributes[(int)PlayerAttribute.Crossing] = 85;
        var source = new PlayerRecord { Address = 1, VtableRva = 2, CurrentAbility = 190, PotentialAbility = 200,
            HomeReputation = 1, CurrentReputation = 2, WorldReputation = 3, Height = 180,
            RawAttributes = attributes, AttributeScale = 5, Positions = new byte[Enum.GetValues<PlayerPosition>().Length],
            Personality = Enumerable.Repeat((byte)12, Enum.GetValues<PersonalityAttribute>().Length).ToArray() };
        var world = new TestWorld();
        var repo = new PlayerRepository(world.Memory, world.Profile, TestWorld.ModuleBase);
        var target = repo.Reload(TestWorld.Mainoo)!;
        var draft = new PlayerDraft(target);
        PlayerAttributeFile.Stage(draft, PlayerAttributeFile.Parse(PlayerAttributeFile.Export(source, false), false));
        Assert.Equal(45, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        var edits = new EditService(new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase), repo);
        draft.Save(edits);
        Assert.Equal(85, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(12, world.Bytes(TestWorld.Mainoo + 0x2F9, 1)[0]);
        Assert.Equal(153, repo.Reload(TestWorld.Mainoo)!.CurrentAbility);
    }
}
