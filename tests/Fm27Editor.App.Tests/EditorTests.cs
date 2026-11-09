using Fm27Editor.App;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Tests;

namespace Fm27Editor.App.Tests;

public sealed class EditorTests
{
    [Fact]
    public void PitchPlacesGoalkeeperBelowStrikerAndLeftPositionsOnLeft()
    {
        var positions = PitchLayout.Positions;
        Assert.Equal(14, positions.Count);
        Assert.Equal(Enum.GetValues<PlayerPosition>().Order(), positions.Select(p => p.Position).Order());
        var goalkeeper = positions.Single(p => p.Position == PlayerPosition.Goalkeeper);
        var striker = positions.Single(p => p.Position == PlayerPosition.Striker);
        // User requested a vertical pitch with GK below ST and left on the left.
        Assert.True(goalkeeper.Y > striker.Y);
        foreach (var (right, left) in new[] { (PlayerPosition.DefenderRight, PlayerPosition.DefenderLeft), (PlayerPosition.WingBackRight, PlayerPosition.WingBackLeft), (PlayerPosition.MidfielderRight, PlayerPosition.MidfielderLeft), (PlayerPosition.AttackingMidRight, PlayerPosition.AttackingMidLeft) })
        {
            Assert.True(positions.Single(p => p.Position == left).X < positions.Single(p => p.Position == right).X);
            Assert.Equal(positions.Single(p => p.Position == left).Y, positions.Single(p => p.Position == right).Y);
        }
    }

    [Fact]
    public void PositionDraftEditsOnlyChosenPositionWhenSaved()
    {
        var (world, _, edits, player) = Setup();
        var draft = new PlayerDraft(player);
        var striker = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == "Striker");
        draft.Stage(striker, 14);
        Assert.Equal(20, world.Bytes(TestWorld.Mainoo + 0x15C, 1)[0]);
        draft.Save(edits);
        Assert.Equal(14, world.Bytes(TestWorld.Mainoo + 0x15C, 1)[0]);
        Assert.Equal(0, world.Bytes(TestWorld.Mainoo + 0x15B, 1)[0]);
    }
    [Fact]
    public void MemoryFailureShowsUsefulMessageWithoutInternalAddress()
    {
        var error = new MemoryAccessException("Read failed at 0x12345678");
        Assert.Equal("Couldn't access game data. Check that a save is loaded and retry.", UserMessage.For(error));
    }

    [Fact]
    public void PartialClauseSaveMessageDoesNotClaimEverythingWasReloaded()
    {
        var error = new Fm27Editor.Core.Clubs.PartialClauseWriteException(new StaleObjectException("changed"));
        Assert.Equal("Some clause fields may have been saved. Reload before retrying.", UserMessage.For(error));
    }
    static (TestWorld world, PlayerRepository repo, EditService edits, PlayerRecord player) Setup()
    {
        var world = new TestWorld();
        var repo = new PlayerRepository(world.Memory, world.Profile, TestWorld.ModuleBase);
        return (world, repo, new EditService(new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase), repo), repo.Reload(TestWorld.Mainoo)!);
    }
    static FieldDescriptor<PlayerRecord> Crossing => PlayerFields.All.Single(f => f.Id == "Crossing");

    [Fact]
    public void DraftDoesNotWriteUntilSaveAndThenWritesAllChangedFields()
    {
        var (world, _, edits, player) = Setup();
        var draft = new PlayerDraft(player);
        var pace = PlayerFields.All.Single(f => f.Id == "Pace");
        draft.Stage(Crossing, 12);
        draft.Stage(pace, 16);
        Assert.Equal(0, world.Memory.Writes);
        Assert.Equal(45, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(66, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        var updated = draft.Save(edits);
        Assert.Equal(60, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(80, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.Equal(2, world.Memory.Writes);
        Assert.Equal(12, updated.Attribute(PlayerAttribute.Crossing));
        Assert.Equal(16, updated.Attribute(PlayerAttribute.Pace));
        Assert.Equal(0, draft.Count);
    }

    /// <summary>Mainoo: born 19 April 2005 (day 109), 184 cm, world reputation 6501.</summary>
    [Fact]
    public void ReputationHeightAndBirthDateAreEditableThroughTheDraft()
    {
        var (world, _, edits, player) = Setup();
        var born = PlayerFields.All.Single(f => f.Id == "Born");
        var height = PlayerFields.All.Single(f => f.Id == "Height");
        var rep = PlayerFields.All.Single(f => f.Id == "WorldReputation");
        Assert.Equal(20050419, born.Read(player));
        Assert.True(DateField.TryParse("2004-03-01", out int newBorn));
        Assert.False(DateField.TryParse("2004-02-30", out _));

        var draft = new PlayerDraft(player);
        draft.Stage(born, newBorn);
        draft.Stage(height, 190);
        draft.Stage(rep, 9600);
        draft.Save(edits);

        // 1 March 2004 = day 61, 2004 = 0x07D4; 9600 = 0x2580.
        Assert.Equal(new byte[] { 61, 0, 0xD4, 0x07 }, world.Bytes(TestWorld.Mainoo + 0x310, 4));
        Assert.Equal(new byte[] { 190, 0 }, world.Bytes(TestWorld.Mainoo + 0x22E, 2));
        Assert.Equal(new byte[] { 0x80, 0x25 }, world.Bytes(TestWorld.Mainoo + 0x262, 2));
    }

    [Fact]
    public void DraftStopsWhenAnotherPendingFieldChangedInGame()
    {
        var (world, repo, edits, player) = Setup();
        var draft = new PlayerDraft(player);
        var pace = PlayerFields.All.Single(f => f.Id == "Pace");
        draft.Stage(Crossing, 12);
        draft.Stage(pace, 16);
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x185, new byte[] { 75 }));
        Assert.Throws<StaleObjectException>(() => draft.Save(edits));
        Assert.Equal(60, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(75, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.Equal(1, draft.Count);
        var rebased = draft.Rebase(repo.Reload(player.Address)!);
        Assert.Equal(16, rebased.Value(pace));
        Assert.Equal(1, rebased.Count);
        Assert.Equal(75, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
    }

    [Fact]
    public void SearchMatchesBothNamesAndUniqueIdWithoutCaseOrAccents()
    {
        var (_, _, _, original) = Setup();
        var player = new PlayerRecord { Address = original.Address, VtableRva = original.VtableRva, Id = 43124203, FirstName = "Bruno", LastName = "Šeško", FullName = "Bruno Šeško", CommonName = "Striker", CurrentAbility = 153, PotentialAbility = 177, HomeReputation = 0, CurrentReputation = 0, WorldReputation = 0, Height = 0, RawAttributes = original.RawAttributes, AttributeScale = 5, Positions = original.Positions, Personality = original.Personality };
        var module = new PlayersModule();
        Assert.True(module.Matches(player, "sesko"));
        Assert.True(module.Matches(player, "STRIKER"));
        Assert.True(module.Matches(player, "43124203"));
        Assert.True(module.Matches(player, "43124"));
        Assert.False(module.Matches(player, "85139014"));
        Assert.False(module.Matches(player, "Yamal"));
    }

    [Fact]
    public void NationRowToStringReturnsNationDisplayNameForComboBoxes()
    {
        var nation = new Fm27Editor.Core.Nations.NationRecord(0x2000, 765, "England", "ENG");
        var row = new NationRow(nation, "England", 765, 4, 155, 19);
        Assert.Equal("England", row.ToString());
    }
    [Fact]
    public void SearchIndexUpdatesResultsWhenQuerySortOrSourceChanges()
    {
        var (_, _, _, original) = Setup();
        PlayerRecord Make(ulong address, string name, short ability) => new()
        {
            Address = address, VtableRva = original.VtableRva, CommonName = name,
            CurrentAbility = ability, PotentialAbility = ability, HomeReputation = 0, CurrentReputation = 0,
            WorldReputation = 0, Height = 180, RawAttributes = original.RawAttributes,
            AttributeScale = 5, Positions = original.Positions, Personality = original.Personality,
        };
        var a = Make(1, "Álvaro", 180);
        var b = Make(2, "Alana", 160);
        IReadOnlyList<object> source = new object[] { a, b };
        var index = new PlayerSearchIndex();
        var module = new PlayersModule();
        var byAbility = new PlayerSortSpec();
        Assert.Equal(new[] { "Álvaro", "Alana" }, index.Filter(source, module, new PlayerFilter { NameQuery = "al" }, byAbility).Select(p => p.DisplayName));
        Assert.Equal(new[] { "Álvaro" }, index.Filter(source, module, new PlayerFilter { NameQuery = "alv" }, byAbility).Select(p => p.DisplayName));
        Assert.Equal(new[] { "Alana", "Álvaro" }, index.Filter(source, module, new PlayerFilter { NameQuery = "al" }, new PlayerSortSpec(PlayerSortKey.Name, false)).Select(p => p.DisplayName));
        IReadOnlyList<object> replacement = new object[] { Make(3, "Alberto", 190) };
        Assert.Equal(new[] { "Alberto" }, index.Filter(replacement, module, new PlayerFilter { NameQuery = "al" }, byAbility).Select(p => p.DisplayName));
    }

    [Fact]
    public async Task LatestSearchDiscardsAnOlderResultThatFinishesAfterTheNewQuery()
    {
        var latest = new LatestSearch();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var old = latest.RunAsync(_ => { started.SetResult(); release.Wait(); return "old"; }, TimeSpan.Zero);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = await latest.RunAsync(_ => "new", TimeSpan.Zero);
        release.Set();
        Assert.Equal("new", newer);
        Assert.Null(await old);
    }

    [Fact]
    public void EditWritesOnlyCrossingAndUndoRestoresIt()
    {
        var (world, _, edits, player) = Setup();
        var before = world.Bytes(TestWorld.Mainoo, 0x3C8);
        var updated = edits.Apply(player, Crossing, 12);
        var after = world.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(60, after[0x15F]);
        Assert.Equal(Enumerable.Range(0, before.Length).Where(i => i != 0x15F).Select(i => before[i]), Enumerable.Range(0, after.Length).Where(i => i != 0x15F).Select(i => after[i]));
        var record = Assert.Single(edits.History);
        Assert.Equal((9, 12, "Crossing", true), (record.OldValue, record.NewValue, record.Field, record.Succeeded));
        Assert.Equal(9, edits.Undo(updated, Crossing, record).Attribute(PlayerAttribute.Crossing));
        Assert.Equal(45, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
    }
    [Fact]
    public void EditReloadsGameChangedAbility()
    {
        var (world, _, edits, player) = Setup();
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x264, BitConverter.GetBytes((short)154)));
        Assert.Equal(154, edits.Apply(player, Crossing, 12).CurrentAbility);
    }
    [Fact]
    public void StaleEditDoesNotOverwriteGameValue()
    {
        var (world, repo, edits, player) = Setup();
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x15F, new byte[] { 50 }));
        Assert.Throws<StaleObjectException>(() => edits.Apply(player, Crossing, 12));
        Assert.Equal(10, repo.Reload(player.Address)!.Attribute(PlayerAttribute.Crossing));
        Assert.Equal(50, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.False(Assert.Single(edits.History).Succeeded);
    }
    [Fact]
    public void RegistryContainsOnlyAvailablePlayersModuleAndNoActions()
    {
        var mod = Assert.Single(Modules.Registered);
        Assert.Equal("Players", mod.Title);
        // User requested Position and Asking Price in the Players list.
        Assert.Equal(new[] { "Name", "Age", "CA", "PA", "Position", "Club", "Nationality", "Asking Price" }, mod.Columns.Select(c => c.Title));
        Assert.Empty(Modules.Actions);
    }

    [Fact]
    public void FilterAndSortSupportsAttributesAgeCaPaClubNationalityAndDivision()
    {
        var (_, _, _, basePlayer) = Setup();
        var gameDate = new DateOnly(2026, 7, 26);

        PlayerRecord Make(ulong addr, string name, DateOnly dob, short ca, short pa, string club, string nation, string league, byte paceRaw, byte finishingRaw, PlayerPosition primaryPos = PlayerPosition.Striker)
        {
            var attrs = (byte[])basePlayer.RawAttributes.Clone();
            attrs[(int)PlayerAttribute.Pace] = paceRaw;
            attrs[(int)PlayerAttribute.Finishing] = finishingRaw;
            var positions = new byte[Enum.GetValues<PlayerPosition>().Length];
            positions[(int)primaryPos] = 20;
            var contract = new ContractInfo(addr + 0x100, 100000, null, null, new ClubInfo(addr + 0x200, club, club));
            return new PlayerRecord
            {
                Address = addr,
                VtableRva = basePlayer.VtableRva,
                CommonName = name,
                FullName = name,
                DateOfBirth = dob,
                GameDate = gameDate,
                CurrentAbility = ca,
                PotentialAbility = pa,
                HomeReputation = 0,
                CurrentReputation = 0,
                WorldReputation = 0,
                Height = 180,
                Contract = contract,
                Nationality = new NationInfo(addr + 0x300, nation, nation[..3].ToUpperInvariant()),
                Division = new CompetitionInfo(addr + 0x400, league, league),
                RawAttributes = attrs,
                AttributeScale = 5,
                Positions = positions,
                Personality = basePlayer.Personality,
            };
        }

        // Mbappé: born 1998-12-20 -> age 27 on 2026-07-26; Pace 20 (100), Finishing 18 (90), ST
        var mbappe = Make(1, "Kylian Mbappé", new DateOnly(1998, 12, 20), 192, 197, "R. Madrid", "France", "LaLiga", 100, 90, PlayerPosition.Striker);
        // Haaland: born 2000-07-21 -> age 26 on 2026-07-26; Pace 19 (95), Finishing 20 (100), ST
        var haaland = Make(2, "Erling Haaland", new DateOnly(2000, 7, 21), 187, 195, "Man City", "Norway", "Premier League", 95, 100, PlayerPosition.Striker);
        // Yamal: born 2007-07-13 -> age 19 on 2026-07-26; Pace 16 (80), Finishing 14 (70), AMR
        var yamal = Make(3, "Lamine Yamal", new DateOnly(2007, 7, 13), 180, 195, "Barcelona", "Spain", "LaLiga", 80, 70, PlayerPosition.AttackingMidRight);
        // Mainoo: born 2005-04-19 -> age 21 on 2026-07-26; Pace 13 (66), Finishing 12 (60), MC
        var mainoo = Make(4, "Kobbie Mainoo", new DateOnly(2005, 4, 19), 153, 177, "Man Utd", "England", "Premier League", 66, 60, PlayerPosition.MidfielderCentral);

        Assert.Equal((27, 26, 19, 21), (mbappe.Age, haaland.Age, yamal.Age, mainoo.Age));

        var all = new[] { mbappe, haaland, yamal, mainoo };
        var module = new PlayersModule();

        // 1. Attribute >= filter (Pace >= 16 and Finishing >= 18) sorted by Finishing descending
        var fastFinishers = module.FilterAndSort(
            all,
            new PlayerFilter
            {
                AttributeThresholds =
                [
                    new(PlayerAttribute.Pace, 16),
                    new(PlayerAttribute.Finishing, 18),
                ]
            },
            new PlayerSortSpec(PlayerSortKey.Attribute, Descending: true, Attribute: PlayerAttribute.Finishing));
        Assert.Equal(new[] { "Erling Haaland", "Kylian Mbappé" }, fastFinishers.Select(p => p.DisplayName));

        // 2. Filter by League ("Premier League") and sort by Age ascending
        var eplByAge = module.FilterAndSort(
            all,
            new PlayerFilter { Division = "Premier League" },
            new PlayerSortSpec(PlayerSortKey.Age, Descending: false));
        Assert.Equal(new[] { "Kobbie Mainoo", "Erling Haaland" }, eplByAge.Select(p => p.DisplayName));

        // 3. Filter by MaxAge <= 21 and MinPa >= 190
        var wonderkids = module.FilterAndSort(
            all,
            new PlayerFilter { MaxAge = 21, MinPa = 190 },
            new PlayerSortSpec(PlayerSortKey.PotentialAbility, Descending: true));
        Assert.Equal(new[] { "Lamine Yamal" }, wonderkids.Select(p => p.DisplayName));

        // 4. Filter by Nationality and Club
        var french = module.FilterAndSort(
            all,
            new PlayerFilter { Nationality = "France", Club = "Madrid" },
            new PlayerSortSpec(PlayerSortKey.CurrentAbility, Descending: true));
        Assert.Equal(new[] { "Kylian Mbappé" }, french.Select(p => p.DisplayName));

        // 5. Filter by Position (AMR or MC)
        var wingersOrMidfielders = module.FilterAndSort(
            all,
            new PlayerFilter { Positions = [PlayerPosition.AttackingMidRight, PlayerPosition.MidfielderCentral], PositionMinRating = 15 },
            new PlayerSortSpec(PlayerSortKey.CurrentAbility, Descending: true));
        Assert.Equal(new[] { "Lamine Yamal", "Kobbie Mainoo" }, wingersOrMidfielders.Select(p => p.DisplayName));

        // Independent thresholds requested by the user: M >= 14 and AM >= 16.
        mainoo.Positions[(int)PlayerPosition.MidfielderCentral] = 14;
        mainoo.Positions[(int)PlayerPosition.AttackingMidCentral] = 16;
        var independent = new PlayerFilter { PositionThresholds = new Dictionary<PlayerPosition, int>
        {
            [PlayerPosition.MidfielderCentral] = 14, [PlayerPosition.AttackingMidCentral] = 16,
        } };
        Assert.True(module.MatchesFilter(mainoo, independent));
        mainoo.Positions[(int)PlayerPosition.AttackingMidCentral] = 15;
        Assert.False(module.MatchesFilter(mainoo, independent));
        Assert.True(module.MatchesFilter(mainoo, new PlayerFilter { NumericRanges = new Dictionary<string, NumericRange>
        {
            ["Height"] = new(180, 185), ["Wages"] = new(100000, 100000)
        } }));
        Assert.False(module.MatchesFilter(mainoo, new PlayerFilter { NumericRanges = new Dictionary<string, NumericRange> { ["Height"] = new(181, null) } }));
        Assert.False(module.MatchesFilter(mainoo, new PlayerFilter { RequiredPreferredMoves = 1 }));

        // PositionRanges: Min and Max range filtering for positions
        mainoo.Positions[(int)PlayerPosition.Striker] = 14;
        var inRangeFilter = new PlayerFilter
        {
            PositionRanges = new Dictionary<PlayerPosition, NumericRange>
            {
                [PlayerPosition.Striker] = new(12, 16)
            }
        };
        Assert.True(module.MatchesFilter(mainoo, inRangeFilter));

        var outOfRangeFilterLow = new PlayerFilter
        {
            PositionRanges = new Dictionary<PlayerPosition, NumericRange>
            {
                [PlayerPosition.Striker] = new(15, 20)
            }
        };
        Assert.False(module.MatchesFilter(mainoo, outOfRangeFilterLow));

        var outOfRangeFilterHigh = new PlayerFilter
        {
            PositionRanges = new Dictionary<PlayerPosition, NumericRange>
            {
                [PlayerPosition.Striker] = new(1, 13)
            }
        };
        Assert.False(module.MatchesFilter(mainoo, outOfRangeFilterHigh));
    }

    [Fact]
    public void FilterPresetStoreUpsertsRemovesAndRoundTripsJson()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fm27-presets-{Guid.NewGuid():N}.json");
        try
        {
            var initial = FilterPresetStore.LoadOrDefaults(tempFile);
            Assert.Contains(initial, p => p.Name == "Fast Striker");

            var custom = new FilterPreset("Elite Winger", [
                new(PlayerAttribute.Dribbling, 16),
                new(PlayerAttribute.Pace, 17),
            ]);
            var withCustom = FilterPresetStore.Upsert(initial, custom);
            Assert.Equal(initial.Count + 1, withCustom.Count);

            var updatedCustom = new FilterPreset("elite winger", [
                new(PlayerAttribute.Dribbling, 18),
            ]);
            var replaced = FilterPresetStore.Upsert(withCustom, updatedCustom);
            Assert.Equal(withCustom.Count, replaced.Count);

            FilterPresetStore.Save(replaced, tempFile);
            var reloaded = FilterPresetStore.LoadOrDefaults(tempFile);
            var winger = Assert.Single(reloaded, p => string.Equals(p.Name, "elite winger", StringComparison.OrdinalIgnoreCase));
            var threshold = Assert.Single(winger.AttributeThresholds);
            Assert.Equal((PlayerAttribute.Dribbling, 18), (threshold.Attribute, threshold.MinValue));

            var afterRemove = FilterPresetStore.Remove(reloaded, "ELITE WINGER");
            Assert.DoesNotContain(afterRemove, p => string.Equals(p.Name, "elite winger", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
