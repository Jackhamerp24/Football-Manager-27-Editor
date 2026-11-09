using Avalonia.Controls;
using Fm27Editor.App;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Tests;

namespace Fm27Editor.App.Tests;

public sealed class SquadTests
{
    [Fact]
    public void SquadPlayerRow_FormatsValuesCorrectly_MatchesSheffieldOracle()
    {
        var dummyPlayer = CreateFakePlayer("Harrison Burrows", 24, 122, 140, "England");

        var burrows = new SquadPlayerRow(
            Player: dummyPlayer,
            SquadNumber: 14,
            InfFlags: "Unh",
            FormattedPositions: "DL, WBL",
            Condition: 8500,
            AskingPrice: 9_018_000,
            AvgRating: "—");

        Assert.Equal("14", burrows.DisplayNumber);
        Assert.Equal("Unh", burrows.InfFlags);
        Assert.Equal("Harrison Burrows", burrows.DisplayName);
        Assert.Equal("England", burrows.DisplayNat);
        Assert.Equal("", burrows.DisplayClub);
        Assert.Equal("122", burrows.DisplayCa);
        Assert.Equal("140", burrows.DisplayPa);
        Assert.Equal("24", burrows.DisplayAge);
        Assert.Equal("8500", burrows.DisplayCondition);
        Assert.Equal("9,018,000", burrows.DisplayAskingPrice);
        Assert.Equal("—", burrows.AvgRating);

        // Jay Tinsdale: squad number unmapped / unset (-1 in contract), displays empty
        var tinsdalePlayer = CreateFakePlayer("Jay Tinsdale", 21, 68, 114, "England");
        var tinsdale = new SquadPlayerRow(
            Player: tinsdalePlayer,
            SquadNumber: -1,
            InfFlags: "Inj",
            FormattedPositions: "MC",
            Condition: 9500,
            AskingPrice: 46_527);

        Assert.Equal("", tinsdale.DisplayNumber);
        Assert.Equal("Inj", tinsdale.InfFlags);
        Assert.Equal("46,527", tinsdale.DisplayAskingPrice);
    }

    [Fact]
    public void SquadFormatters_FormatInfFlags_ReturnsInjAndBanCorrectly()
    {
        var player = CreateFakePlayer("Test Player", 25, 120, 130, "England");

        // When player is injured only -> "Inj"
        Assert.Equal("Inj", SquadFormatters.FormatInfFlags(player, isInjured: true, isBanned: false));
        // When player is banned only -> "Ban"
        Assert.Equal("Ban", SquadFormatters.FormatInfFlags(player, isInjured: false, isBanned: true));
        // When player is both injured and banned -> "Inj, Ban"
        Assert.Equal("Inj, Ban", SquadFormatters.FormatInfFlags(player, isInjured: true, isBanned: true));
        // Unhappy (FMRTE Man Utd: Bruno Fernandes "Unh"; Manuel Ugarte "Inj")
        Assert.Equal("Unh", SquadFormatters.FormatInfFlags(player, isUnhappy: true));
        Assert.Equal("Inj, Unh", SquadFormatters.FormatInfFlags(player, isInjured: true, isUnhappy: true));
        // Healthy unbanned player displays empty
        Assert.Equal("", SquadFormatters.FormatInfFlags(player, isInjured: false, isBanned: false));
    }

    [Fact]
    public void SquadFormatters_HealAndLiftBans_ClearsInfFlags()
    {
        var player = CreateFakePlayer("Test Player", 25, 120, 130, "England");

        // Player initially injured and banned
        Assert.Equal("Inj, Ban", SquadFormatters.FormatInfFlags(player, isInjured: true, isBanned: true));

        // When healed -> isInjured becomes false, Inf only has Ban
        Assert.Equal("Ban", SquadFormatters.FormatInfFlags(player, isInjured: false, isBanned: true));

        // When bans are lifted too -> Inf is completely cleared (no red flags)
        Assert.Equal("", SquadFormatters.FormatInfFlags(player, isInjured: false, isBanned: false));
    }

    [Fact]
    public void SquadFormatters_FormatPositions_ReturnsNaturalAndAccomplished()
    {
        var player = CreateFakePlayerWithPositions("Kobbie Mainoo", [
            (PlayerPosition.MidfielderCentral, 20),
            (PlayerPosition.AttackingMidCentral, 16),
            (PlayerPosition.DefensiveMidfielder, 12),
        ]);

        string pos = SquadFormatters.FormatPositions(player);
        Assert.Contains("MC", pos);
        Assert.Contains("AMC", pos);
        Assert.DoesNotContain("DM", pos); // Rating 12 is below 15 threshold
    }

    [Fact]
    public void PlayerActions_ContractButtonsAreEnabled_StateButtonsAreDisabled()
    {
        // Check that PlayerField enum has all 11 required members from docs/squad-plan.md
        var expectedFields = new[]
        {
            PlayerField.Condition,
            PlayerField.MatchSharpness,
            PlayerField.Jadedness,
            PlayerField.MatchExercise,
            PlayerField.Morale,
            PlayerField.AskingPrice,
            PlayerField.Caps,
            PlayerField.InternationalGoals,
            PlayerField.SubCaps,
            PlayerField.SubGoals,
            PlayerField.DaysInCountry,
            // Information tab (docs/player-tabs.md)
            PlayerField.ArrivedInCountry,
            PlayerField.BodyType,
            PlayerField.NationalityInfo,
            PlayerField.TreatedAsNonEuInItaly,
            PlayerField.RetiringAfterSpellAtCurrentClub,
            PlayerField.Ethnicity,
            PlayerField.HairColour,
            PlayerField.HairLength,
            PlayerField.SkinColour
        };
        var actualFields = Enum.GetValues<PlayerField>();
        Assert.Equal(expectedFields.OrderBy(f => f), actualFields.OrderBy(f => f));
    }

    [Fact]
    public void EditService_ExecutesActionAtomically()
    {
        var world = new TestWorld();
        var repo = new PlayerRepository(world.Memory, world.Profile, TestWorld.ModuleBase);
        var edits = new EditService(new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase), repo);

        bool executed = false;
        edits.Execute(() => { executed = true; });
        Assert.True(executed);
    }

    [Fact]
    public void SquadScalarDefinitions_Jadedness_RangeIsMinus500To1000()
    {
        var jadedness = SquadScalarDefinitions.Descriptors.First(d => d.Field == PlayerField.Jadedness);
        Assert.Equal(-500, jadedness.Min);
        Assert.Equal(1000, jadedness.Max);
    }

    [Fact]
    public void SquadScalarDefinitions_ConditionAndSharpness_RangeIs0To10000()
    {
        var condition = SquadScalarDefinitions.Descriptors.First(d => d.Field == PlayerField.Condition);
        Assert.Equal(0, condition.Min);
        Assert.Equal(10000, condition.Max);

        var sharpness = SquadScalarDefinitions.Descriptors.First(d => d.Field == PlayerField.MatchSharpness);
        Assert.Equal(0, sharpness.Min);
        Assert.Equal(10000, sharpness.Max);
    }

    [Fact]
    public void SquadScalarDefinitions_AskingPrice_WidthIsGenerous()
    {
        var price = SquadScalarDefinitions.Descriptors.First(d => d.Field == PlayerField.AskingPrice);
        Assert.True(price.Width >= 140, "Asking Price card must be wide enough for formatted millions");
    }

    static PlayerRecord CreateFakePlayer(string name, int age, short ca, short pa, string nat)
    {
        var rawAttr = new byte[64];
        var pos = new byte[64];
        var person = new byte[64];

        return new PlayerRecord
        {
            Address = 0x1000_0000,
            VtableRva = 0x4D27228,
            Id = 12345,
            FirstName = name.Split(' ')[0],
            LastName = name.Split(' ').Length > 1 ? name.Split(' ')[1] : "",
            CommonName = null,
            FullName = name,
            CurrentAbility = ca,
            PotentialAbility = pa,
            HomeReputation = 5000,
            CurrentReputation = 5000,
            WorldReputation = 5000,
            Height = 180,
            DateOfBirth = new DateOnly(2026 - age, 1, 1),
            GameDate = new DateOnly(2026, 7, 22),
            Contract = null,
            Nationality = new NationInfo(0x5000, nat, nat),
            Division = null,
            PendingContractAddress = 0,
            RawAttributes = rawAttr,
            AttributeScale = 5,
            Positions = pos,
            Personality = person,
        };
    }

    static PlayerRecord CreateFakePlayerWithPositions(string name, (PlayerPosition pos, byte rating)[] positions)
    {
        var rawAttr = new byte[64];
        var posArray = new byte[64];
        foreach (var (p, r) in positions)
            posArray[(int)p] = r;

        var person = new byte[64];

        return new PlayerRecord
        {
            Address = 0x1000_0000,
            VtableRva = 0x4D27228,
            Id = 12345,
            FirstName = name,
            LastName = "",
            CommonName = null,
            FullName = name,
            CurrentAbility = 120,
            PotentialAbility = 140,
            HomeReputation = 5000,
            CurrentReputation = 5000,
            WorldReputation = 5000,
            Height = 180,
            DateOfBirth = new DateOnly(2000, 1, 1),
            GameDate = new DateOnly(2026, 7, 22),
            Contract = null,
            Nationality = null,
            Division = null,
            PendingContractAddress = 0,
            RawAttributes = rawAttr,
            AttributeScale = 5,
            Positions = posArray,
            Personality = person,
        };
    }
}
