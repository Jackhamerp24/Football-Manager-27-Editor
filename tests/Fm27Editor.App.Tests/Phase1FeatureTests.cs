using Avalonia.Controls;
using Avalonia.VisualTree;
using Fm27Editor.App;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App.Tests;

public sealed class Phase1FeatureTests
{
    static PlayerRecord MakePlayer(
        ulong addr,
        int id,
        string name,
        DateOnly dob,
        short ca,
        short pa,
        PlayerPosition naturalPos = PlayerPosition.Striker,
        byte uniformAttr20 = 12,
        Action<byte[], byte[]>? customize = null)
    {
        var raw = new byte[64];
        Array.Fill(raw, (byte)(uniformAttr20 * 5));
        raw[(int)PlayerAttribute.RightFoot] = 100;
        raw[(int)PlayerAttribute.LeftFoot] = 60;
        var pers = new byte[8];
        Array.Fill(pers, (byte)10);
        var positions = new byte[14];
        positions[(int)naturalPos] = 20;
        customize?.Invoke(raw, pers);
        return new PlayerRecord
        {
            Address = addr,
            VtableRva = 1,
            Id = id,
            CommonName = name,
            FullName = name,
            DateOfBirth = dob,
            GameDate = new DateOnly(2026, 7, 1),
            CurrentAbility = ca,
            PotentialAbility = pa,
            HomeReputation = 100,
            CurrentReputation = 100,
            WorldReputation = 100,
            Height = 180,
            RawAttributes = raw,
            AttributeScale = 5,
            Positions = positions,
            Personality = pers,
        };
    }

    static T FindSingle<T>(Control root) where T : Control
    {
        if (root is T hit) return hit;
        if (root is Panel panel)
            foreach (var child in panel.Children)
                if (child is Control c && TryFind(c, out T? found))
                    return found!;
        if (root is Decorator dec && dec.Child is Control dc && TryFind(dc, out T? decFound))
            return decFound!;
        if (root is ContentControl cc && cc.Content is Control ccChild && TryFind(ccChild, out T? ccFound))
            return ccFound!;
        throw new Xunit.Sdk.XunitException($"Control {typeof(T).Name} not found.");
    }

    static bool TryFind<T>(Control root, out T? found) where T : Control
    {
        if (root is T hit) { found = hit; return true; }
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c && TryFind(c, out found))
                    return true;
        }
        if (root is Decorator dec && dec.Child is Control dc && TryFind(dc, out found))
            return true;
        if (root is ContentControl cc && cc.Content is Control ccChild && TryFind(ccChild, out found))
            return true;
        found = null;
        return false;
    }

    [Fact]
    public void PlayersModuleSortsByRcaBestRoleSpecificRoleAndPersonality()
    {
        var mod = new PlayersModule();
        var highRca = MakePlayer(1, 100, "High RCA", new DateOnly(2002, 1, 1), 160, 170, PlayerPosition.Striker, 16);
        var midRca = MakePlayer(2, 101, "Mid RCA", new DateOnly(2002, 1, 1), 140, 160, PlayerPosition.MidfielderCentral, 12);
        var lowRca = MakePlayer(3, 102, "Low RCA", new DateOnly(2002, 1, 1), 110, 150, PlayerPosition.DefenderCentral, 9,
            (raw, pers) =>
            {
                // Make Ambitious (id=0) instead of Driven/Resolute so personality name sorts alphabetically before Balanced/Resolute
                pers[(int)PersonalityAttribute.Ambition] = 17;
                pers[(int)PersonalityAttribute.Loyalty] = 5;
                raw[(int)PlayerAttribute.Determination] = 5 * 5;
            });

        var all = new[] { midRca, lowRca, highRca };

        var byRca = mod.FilterAndSort(all, new PlayerFilter(), new PlayerSortSpec(PlayerSortKey.Rca, Descending: true));
        Assert.Equal(["High RCA", "Mid RCA", "Low RCA"], byRca.Select(p => p.DisplayName));

        var byBestRole = mod.FilterAndSort(all, new PlayerFilter(), new PlayerSortSpec(PlayerSortKey.BestRoleScore, Descending: true));
        Assert.Equal(["High RCA", "Mid RCA", "Low RCA"], byBestRole.Select(p => p.DisplayName));

        var byGkRole = mod.FilterAndSort(all, new PlayerFilter(), new PlayerSortSpec(PlayerSortKey.RoleScore, Descending: true, RoleCode: "SK (D)"));
        Assert.Equal(["High RCA", "Mid RCA", "Low RCA"], byGkRole.Select(p => p.DisplayName));

        var byPersAsc = mod.FilterAndSort(all, new PlayerFilter(), new PlayerSortSpec(PlayerSortKey.Personality, Descending: false));
        Assert.Equal("Ambitious", PersonalityClassifier.Classify(byPersAsc[0], isRegenOverride: false).Name);
    }

    [Fact]
    public void PlayersModuleFiltersByRcaRangeRoleScoreAndPersonalityPolarity()
    {
        int? previousFloor = RegenIds.Current;
        RegenIds.Current = 1_000_000;
        try
        {
            var mod = new PlayersModule();
            var gem = MakePlayer(1, 1_000_100, "Model Pro", new DateOnly(2006, 5, 1), 150, 190, PlayerPosition.Striker, 15,
                (raw, pers) =>
                {
                    pers[(int)PersonalityAttribute.Ambition] = 14;
                    pers[(int)PersonalityAttribute.Loyalty] = 14;
                    pers[(int)PersonalityAttribute.Pressure] = 14;
                    pers[(int)PersonalityAttribute.Professionalism] = 20;
                    pers[(int)PersonalityAttribute.Sportsmanship] = 14;
                    pers[(int)PersonalityAttribute.Temperament] = 14;
                    raw[(int)PlayerAttribute.Determination] = 14 * 5;
                });

            var slackRegen = MakePlayer(2, 1_000_200, "Slack Regen", new DateOnly(2008, 3, 10), 120, 175, PlayerPosition.MidfielderCentral, 11,
                (raw, pers) =>
                {
                    pers[(int)PersonalityAttribute.Professionalism] = 1;
                    pers[(int)PersonalityAttribute.Temperament] = 10;
                    raw[(int)PlayerAttribute.Determination] = 5 * 5;
                });

            Assert.False(PersonalityClassifier.Classify(gem).IsNegative);
            Assert.True(PersonalityClassifier.Classify(slackRegen).IsNegative);
            Assert.Equal("Slack", PersonalityClassifier.Classify(slackRegen).Name);

            var all = new[] { gem, slackRegen };

            var noNegative = mod.FilterAndSort(all, new PlayerFilter
            {
                NumericRanges = new Dictionary<string, NumericRange> { ["Personality"] = new(-2, -2) }
            }, new PlayerSortSpec());
            Assert.Equal(["Model Pro"], noNegative.Select(p => p.DisplayName));

            var onlyNegative = mod.FilterAndSort(all, new PlayerFilter
            {
                NumericRanges = new Dictionary<string, NumericRange> { ["Personality"] = new(-3, -3) }
            }, new PlayerSortSpec());
            Assert.Equal(["Slack Regen"], onlyNegative.Select(p => p.DisplayName));

            var rcaFiltered = mod.FilterAndSort(all, new PlayerFilter
            {
                NumericRanges = new Dictionary<string, NumericRange> { ["RCA"] = new(RcaCalculator.Calculate(gem) - 1, null) }
            }, new PlayerSortSpec());
            Assert.Equal(["Model Pro"], rcaFiltered.Select(p => p.DisplayName));

            var roleFiltered = mod.FilterAndSort(all, new PlayerFilter
            {
                RoleCode = "AF (A)",
                MinRoleScore = 70.0,
            }, new PlayerSortSpec());
            Assert.Equal(["Model Pro"], roleFiltered.Select(p => p.DisplayName));
        }
        finally
        {
            RegenIds.Current = previousFloor;
        }
    }

    [Fact]
    public void MiniScoutingCardAndTalentedRegensTabRenderOnAvaloniaUiThread()
    {
        AvaloniaTestThread.Run(() =>
        {
            PlayerRecord? opened = null;
            var tab = new TalentedRegensTab(p => opened = p);
            var regen1 = MakePlayer(1, 2_000_001, "Wonder Regen", new DateOnly(2007, 4, 1), 130, 185, PlayerPosition.Striker, 14);
            var regen2 = MakePlayer(2, 2_000_002, "Low PA Regen", new DateOnly(2007, 4, 1), 100, 120, PlayerPosition.Striker, 10);
            var realVeteran = MakePlayer(3, 1_234, "Veteran Real", new DateOnly(1996, 4, 1), 160, 165, PlayerPosition.Striker, 16);

            var card = MainWindow.MiniScoutingCard(regen1);
            Assert.NotNull(card);

            tab.SetPlayers([regen1, regen2, realVeteran], regenIdFloorOverride: 2_000_000);
            var grid = FindSingle<DataGrid>(tab.Root);
            var items = grid.ItemsSource!.Cast<TalentedRegensTab.RegenRow>().ToList();
            Assert.Single(items);
            Assert.Equal("Wonder Regen", items[0].Name);
            Assert.Null(opened);
        });
    }

    [Fact]
    public void StaffRcaReportsUnavailableWhenDataAnalystAtOrAbove15OrRolesMissing()
    {
        var attrs = new byte[Enum.GetValues<StaffAttribute>().Length];
        Array.Fill(attrs, (byte)15); // 15/20 across the board -> expected uniform coach RCA = 150
        var analystRoles = new byte[15];
        analystRoles[(int)StaffRole.DataAnalyst] = 18;
        var analyst = new StaffRecord
        {
            Address = 0x100,
            StaffAddress = 0x200,
            Attributes = attrs,
            Personality = new byte[8],
            Roles = analystRoles,
            Kind = new StaffKind(0, 0, 0, false),
        };
        var analystRca = StaffRcaCalculator.Calculate(analyst);
        Assert.Null(analystRca.Score);
        Assert.True(analystRca.HasUnavailableDataAnalystInput);

        var coachRoles = new byte[15];
        coachRoles[(int)StaffRole.Coach] = 20;
        var coach = new StaffRecord
        {
            Address = 0x101,
            StaffAddress = 0x201,
            Attributes = attrs,
            Personality = new byte[8],
            Roles = coachRoles,
            Kind = new StaffKind(0, 0, 0, false),
        };
        var coachRca = StaffRcaCalculator.Calculate(coach);
        Assert.NotNull(coachRca.Score);
        Assert.False(coachRca.HasUnavailableDataAnalystInput);
    }
}
