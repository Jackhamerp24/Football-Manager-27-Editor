using Fm27Editor.App;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Tests;

namespace Fm27Editor.App.Tests;

public sealed class ClubTests
{
    const uint ClubVt = 0x4CDA1F8, TeamVt = 0x4CE1108;
    const ulong ClubAddr = 0x5000_0000, DetailAddr = 0x5001_0000, FinanceAddr = 0x5002_0000;
    const ulong FirstTeamAddr = 0x5003_0000, U21TeamAddr = 0x5004_0000;

    sealed class RecordingEditService(IEditService inner) : IEditService
    {
        public int ExecuteCalls { get; private set; }
        public IReadOnlyList<EditRecord> History => inner.History;
        public PlayerRecord Apply(PlayerRecord player, FieldDescriptor<PlayerRecord> field, int value) => inner.Apply(player, field, value);
        public PlayerRecord Undo(PlayerRecord player, FieldDescriptor<PlayerRecord> field, EditRecord record) => inner.Undo(player, field, record);
        public void Execute(Action action)
        {
            ExecuteCalls++;
            inner.Execute(action);
        }
    }

    static string ProfileJsonWithoutShippedFields() =>
        File.ReadAllText(TestWorld.ProfilePath).Replace("\"fields\": {", "\"unusedFields\": {");

    static OffsetProfile ProfileWithFields(string fieldsJsonBody)
    {
        string raw = ProfileJsonWithoutShippedFields();
        string injected = $"\"fields\": {{ {fieldsJsonBody} }}, \"transferClauses\": {{";
        return OffsetProfile.Load(raw.Replace("\"transferClauses\": {", injected));
    }

    static (FakeMemory Memory, RecordingEditService Edits) CreateSheffieldMemory()
    {
        var world = new TestWorld();
        var m = world.Memory;

        var club = m.Map(ClubAddr, 0x160);
        TestWorld.Set64(club, 0x00, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(club, 0x100, DetailAddr);
        TestWorld.Set64(club, 0x150, FinanceAddr);

        // Sheffield United values from FMRTE 27 (docs/club-plan.md):
        // YearFounded 1889 (+0xD8), ChairmanStatus 6916 (+0xDE), AllowCustomLogo 1 (+0xE0),
        // EmbargoStart 1/1/1900 (+0xE4), SupportersCore 40% stored x10 = 400 (+0xE8),
        // BackgroundColour dark red 0xFF8B0000 (+0xEC), Training 13 (+0xF0).
        var detail = m.Map(DetailAddr, 0x110);
        BitConverter.GetBytes(1889).CopyTo(detail, 0xD8);
        BitConverter.GetBytes((short)6916).CopyTo(detail, 0xDE);
        detail[0xE0] = 1;
        Convert.FromHexString("01006C07").CopyTo(detail, 0xE4);
        BitConverter.GetBytes((short)400).CopyTo(detail, 0xE8);
        BitConverter.GetBytes(0xFF8B0000u).CopyTo(detail, 0xEC);
        detail[0xF0] = 13;

        // Finance: full finance vtable 0x4CF0CC8, WageUsed 490710 (+0x814), TransferBudgetRemaining 6000000 (+0x7C4).
        var finance = m.Map(FinanceAddr, 0x900);
        TestWorld.Set64(finance, 0x00, TestWorld.ModuleBase + 0x4CF0CC8);
        BitConverter.GetBytes(6_000_000).CopyTo(finance, 0x7C4);
        BitConverter.GetBytes(490_710).CopyTo(finance, 0x814);

        // Tactical Understanding PassingStyle (+0x80): First team 60, U21 62; First team Reputation 6200 (+0x20).
        var firstTeam = m.Map(FirstTeamAddr, 0x100);
        TestWorld.Set64(firstTeam, 0x00, TestWorld.ModuleBase + TeamVt);
        BitConverter.GetBytes((short)6200).CopyTo(firstTeam, 0x20);
        firstTeam[0x80] = 60;

        var u21 = m.Map(U21TeamAddr, 0x100);
        TestWorld.Set64(u21, 0x00, TestWorld.ModuleBase + TeamVt);
        BitConverter.GetBytes((short)4100).CopyTo(u21, 0x20);
        u21[0x80] = 62;

        var repo = new PlayerRepository(m, world.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(m, world.Profile, TestWorld.ModuleBase);
        var edits = new RecordingEditService(new EditService(writer, repo));
        return (m, edits);
    }

    [Fact]
    public void Section_order_and_every_club_field_metadata_matches_fmrte()
    {
        Assert.Equal(
            [ClubSection.Information, ClubSection.Finances, ClubSection.Supporters, ClubSection.TacticalUnderstanding],
            ClubFieldInfo.ScalarSections);
        Assert.Equal(
            [
                ClubSection.ClubVision,
                ClubSection.Affiliations,
                ClubSection.RegionalDivisions,
                ClubSection.Kits,
                ClubSection.TransferCenter,
                ClubSection.Relationships,
                ClubSection.Staff,
            ],
            ClubFieldInfo.TopLevelListSections);
        Assert.Equal(
            [
                ClubSection.Finances,
                ClubSection.Sponsors,
                ClubSection.Debts,
            ],
            ClubFieldInfo.FinanceSubSections);
        Assert.Equal(
            [
                ClubSection.ClubVision,
                ClubSection.Affiliations,
                ClubSection.RegionalDivisions,
                ClubSection.Sponsors,
                ClubSection.Debts,
                ClubSection.Kits,
                ClubSection.TransferCenter,
                ClubSection.Relationships,
                ClubSection.Staff,
            ],
            ClubFieldInfo.ListSections);
        Assert.Equal(
            [
                ClubSection.Information,
                ClubSection.Squads,
                ClubSection.Finances,
                ClubSection.Supporters,
                ClubSection.ClubVision,
                ClubSection.Affiliations,
                ClubSection.RegionalDivisions,
                ClubSection.Kits,
                ClubSection.TransferCenter,
                ClubSection.Relationships,
                ClubSection.Staff,
            ],
            ClubFieldInfo.Sections);
        Assert.Equal(
            [
                "Information",
                "Squads",
                "Finances",
                "Supporters",
                "Club Vision",
                "Affiliations",
                "Regional Divisions",
                "Kits and Colours",
                "Transfer Center",
                "Relationships",
                "Staff",
            ],
            ClubFieldInfo.Sections.Select(ClubFieldInfo.SectionTitle));
        Assert.Equal(
            ["Finances", "Sponsors and Other", "Debt and Loans"],
            ClubFieldInfo.FinanceSubSections.Select(ClubFieldInfo.SectionTitle));
        Assert.All(ClubFieldInfo.ScalarSections, s => Assert.False(ClubFieldInfo.IsListSection(s)));
        Assert.All(ClubFieldInfo.ListSections, s => Assert.True(ClubFieldInfo.IsListSection(s)));

        var allEnums = Enum.GetValues<ClubField>();
        Assert.Equal(allEnums.Order(), ClubFieldInfo.All.Select(m => m.Field).Order());
        Assert.All(ClubFieldInfo.All, meta =>
        {
            Assert.False(string.IsNullOrWhiteSpace(meta.Label));
            Assert.False(string.IsNullOrWhiteSpace(meta.Group));
            Assert.True(meta.Min <= meta.Max, $"{meta.Field}: {meta.Min} > {meta.Max}");
        });

        Assert.Equal(("Year Founded", ClubSection.Information, "Overview"),
            (ClubFieldInfo.For(ClubField.YearFounded).Label, ClubFieldInfo.For(ClubField.YearFounded).Section, ClubFieldInfo.For(ClubField.YearFounded).Group));
        Assert.Equal(("% of Transfer Revenue Made Available", ClubSection.Finances, "Overview"),
            (ClubFieldInfo.For(ClubField.TransferRevenuePercent).Label, ClubFieldInfo.For(ClubField.TransferRevenuePercent).Section, ClubFieldInfo.For(ClubField.TransferRevenuePercent).Group));
        Assert.Equal(("Average Match Ticket Price", ClubSection.Finances, "Overview"),
            (ClubFieldInfo.For(ClubField.AverageTicketPrice).Label, ClubFieldInfo.For(ClubField.AverageTicketPrice).Section, ClubFieldInfo.For(ClubField.AverageTicketPrice).Group));
        Assert.Equal(("Fair Weather", ClubSection.Supporters, "Distribution"),
            (ClubFieldInfo.For(ClubField.SupportersFairWeather).Label, ClubFieldInfo.For(ClubField.SupportersFairWeather).Section, ClubFieldInfo.For(ClubField.SupportersFairWeather).Group));
        Assert.Equal(("Position / Role", ClubSection.TacticalUnderstanding, "Tactical Understanding"),
            (ClubFieldInfo.For(ClubField.PositionRole).Label, ClubFieldInfo.For(ClubField.PositionRole).Section, ClubFieldInfo.For(ClubField.PositionRole).Group));
    }

    [Fact]
    public void Formatting_and_parsing_match_sheffield_united_fmrte_values()
    {
        // Year founded has no thousands comma (1889), whereas status/attendance/money use N0.
        Assert.Equal("1889", ClubFieldInfo.FormatValue(1889, ClubDisplayKind.PlainInt));
        Assert.Equal("6,916", ClubFieldInfo.FormatValue(6916, ClubDisplayKind.FormattedInt));
        Assert.Equal("574,287", ClubFieldInfo.FormatValue(574287, ClubDisplayKind.FormattedInt));
        Assert.Equal("6,000,000", ClubFieldInfo.FormatValue(6000000, ClubDisplayKind.Money));

        // 1/1/1900 is represented as 0 ("no date").
        Assert.Equal("", ClubFieldInfo.FormatValue(0, ClubDisplayKind.Date));
        Assert.Equal("2026-08-01", ClubFieldInfo.FormatValue(20260801, ClubDisplayKind.Date));
        Assert.True(ClubFieldInfo.TryParseDate("", out long noneEmpty) && noneEmpty == 0);
        Assert.True(ClubFieldInfo.TryParseDate("1900-01-01", out long none1900) && none1900 == 0);
        Assert.True(ClubFieldInfo.TryParseDate("2026-08-01", out long aug1) && aug1 == 20260801);
        Assert.False(ClubFieldInfo.TryParseDate("2026-02-30", out _));

        // Colours preserve the original alpha byte when edited as 6-digit #RRGGBB, and accept 8-digit #AARRGGBB.
        Assert.Equal("#8B0000", ClubFieldInfo.FormatValue(0xFF8B0000, ClubDisplayKind.Colour));
        Assert.True(ClubFieldInfo.TryParseColour("#FFFFFF", 0xFF8B0000, out long opaqueWhite));
        Assert.Equal(0xFFFFFFFFL, opaqueWhite);

        Assert.True(ClubFieldInfo.TryParseColour("#FFFFFF", 0x008B0000, out long zeroAlphaWhite));
        Assert.Equal(0x00FFFFFFL, zeroAlphaWhite);

        Assert.Equal("#808B0000", ClubFieldInfo.FormatValue(0x808B0000, ClubDisplayKind.Colour));
        Assert.True(ClubFieldInfo.TryParseColour("#808B0000", 0xFF000000, out long semiRed));
        Assert.Equal(0x808B0000L, semiRed);
        Assert.False(ClubFieldInfo.TryParseColour("#GGGGGG", 0xFF8B0000, out _));
    }

    [Fact]
    public void Club_search_matches_name_without_case_or_accents_and_ranks_exact_id_first()
    {
        var sheffield = new ClubEntry(1, 708, "Sheffield United", "Sheff Utd", 0, []);
        var almeria = new ClubEntry(2, 1661, "UD Almería", "Almería", 0, []);
        var psg = new ClubEntry(3, 868, "Paris Saint-Germain", "PSG", 0, []);
        var club7080 = new ClubEntry(4, 7080, "Club 7080", "Club 7080", 0, []);
        var clubs = new[] { psg, club7080, almeria, sheffield };

        var byId = ClubCatalog.Filter(clubs, "708");
        Assert.Equal([708, 7080], byId.Select(c => c.Id!.Value));

        var byAccent = ClubCatalog.Filter(clubs, "almeria");
        Assert.Equal(1661, Assert.Single(byAccent).Id);

        var byFullName = ClubCatalog.Filter(clubs, "SAINT-GERMAIN");
        Assert.Equal(868, Assert.Single(byFullName).Id);

        Assert.Equal("Sheffield United, 708", new MainWindow.ClubPickerItem(sheffield with { ShortName = "Sheffield United" }).ToString());
        Assert.Equal("Sheff Utd, 708", new MainWindow.ClubPickerItem(sheffield).ToString());
        Assert.DoesNotContain("Address", new MainWindow.ClubPickerItem(sheffield).ToString());
    }

    [Fact]
    public void Only_mapped_fields_appear_and_newly_mapped_profile_fields_appear_without_code_changes()
    {
        var (m, _) = CreateSheffieldMemory();

        // 1. Profile with no club.fields: every section renders empty.
        var emptyProfile = OffsetProfile.Load(ProfileJsonWithoutShippedFields());
        var emptyAccess = new ClubFieldAccess(m, emptyProfile, TestWorld.ModuleBase);
        var emptyValues = emptyAccess.ReadAll(ClubAddr, FirstTeamAddr);
        foreach (var section in ClubFieldInfo.Sections)
            Assert.Empty(ClubSectionLayout.BuildGroups(section, emptyProfile, emptyAccess, ClubAddr, FirstTeamAddr, emptyValues));

        // 2. Profile maps only YearFounded and PassingStyle.
        var partialProfile = ProfileWithFields("""
            "YearFounded": { "root": "club", "path": "0x100->0xD8", "type": "int32" },
            "PassingStyle": { "root": "team", "path": "0x80", "type": "byte" }
            """);
        var partialAccess = new ClubFieldAccess(m, partialProfile, TestWorld.ModuleBase);
        var partialValues = partialAccess.ReadAll(ClubAddr, FirstTeamAddr);

        var infoGroups = ClubSectionLayout.BuildGroups(ClubSection.Information, partialProfile, partialAccess, ClubAddr, FirstTeamAddr, partialValues);
        var overview = Assert.Single(infoGroups);
        Assert.Equal("Overview", overview.Title);
        Assert.Equal([ClubField.YearFounded], overview.Fields.Select(f => f.Field));
        Assert.Empty(ClubSectionLayout.BuildGroups(ClubSection.Finances, partialProfile, partialAccess, ClubAddr, FirstTeamAddr, partialValues));
        Assert.Empty(ClubSectionLayout.BuildGroups(ClubSection.Supporters, partialProfile, partialAccess, ClubAddr, FirstTeamAddr, partialValues));

        var tacticalGroups = ClubSectionLayout.BuildGroups(ClubSection.TacticalUnderstanding, partialProfile, partialAccess, ClubAddr, FirstTeamAddr, partialValues);
        var tactical = Assert.Single(tacticalGroups);
        Assert.True(tactical.UsesTeamRoot);
        Assert.Equal([ClubField.PassingStyle], tactical.Fields.Select(f => f.Field));

        // 3. Expanding profile JSON (no code change) immediately surfaces new groups and fields.
        var expandedProfile = ProfileWithFields("""
            "YearFounded": { "root": "club", "path": "0x100->0xD8", "type": "int32" },
            "Training": { "root": "club", "path": "0x100->0xF0", "type": "byte" },
            "WageUsed": { "root": "club", "path": "0x150->0x814", "type": "int32" },
            "SupportersCore": { "root": "club", "path": "0x100->0xE8", "type": "int16", "scale": 10 },
            "PassingStyle": { "root": "team", "path": "0x80", "type": "byte" }
            """);
        var expandedAccess = new ClubFieldAccess(m, expandedProfile, TestWorld.ModuleBase);
        var expandedValues = expandedAccess.ReadAll(ClubAddr, FirstTeamAddr);

        Assert.Equal(["Overview", "Facilities"],
            ClubSectionLayout.BuildGroups(ClubSection.Information, expandedProfile, expandedAccess, ClubAddr, FirstTeamAddr, expandedValues).Select(g => g.Title));
        Assert.Equal([ClubField.WageUsed],
            Assert.Single(ClubSectionLayout.BuildGroups(ClubSection.Finances, expandedProfile, expandedAccess, ClubAddr, FirstTeamAddr, expandedValues)).Fields.Select(f => f.Field));
        Assert.Equal([ClubField.SupportersCore],
            Assert.Single(ClubSectionLayout.BuildGroups(ClubSection.Supporters, expandedProfile, expandedAccess, ClubAddr, FirstTeamAddr, expandedValues)).Fields.Select(f => f.Field));
    }

    [Fact]
    public void Draft_stages_without_writing_and_saves_club_and_per_team_fields_through_edits_execute()
    {
        var (m, edits) = CreateSheffieldMemory();
        var profile = ProfileWithFields("""
            "YearFounded": { "root": "club", "path": "0x100->0xD8", "type": "int32" },
            "EmbargoStart": { "root": "club", "path": "0x100->0xE4", "type": "date" },
            "BackgroundColour": { "root": "club", "path": "0x100->0xEC", "type": "colour" },
            "PassingStyle": { "root": "team", "path": "0x80", "type": "byte" }
            """);
        var access = new ClubFieldAccess(m, profile, TestWorld.ModuleBase);
        var draft = new ClubDraft(ClubAddr);

        // Read First Team snapshot (PassingStyle = 60).
        var firstValues = access.ReadAll(ClubAddr, FirstTeamAddr);
        draft.AbsorbSnapshot(ClubAddr, FirstTeamAddr, profile, firstValues);
        var infoFields = ClubSectionLayout.BuildGroups(ClubSection.Information, profile, access, ClubAddr, FirstTeamAddr, firstValues)
            .SelectMany(g => g.Fields).ToDictionary(f => f.Field);
        var financeFields = ClubSectionLayout.BuildGroups(ClubSection.Finances, profile, access, ClubAddr, FirstTeamAddr, firstValues)
            .SelectMany(g => g.Fields).ToDictionary(f => f.Field);
        var firstTactical = Assert.Single(ClubSectionLayout.BuildGroups(ClubSection.TacticalUnderstanding, profile, access, ClubAddr, FirstTeamAddr, firstValues))
            .Fields.Single();

        // Stage then revert YearFounded -> removed from pending changes.
        draft.Stage(infoFields[ClubField.YearFounded], 1900);
        Assert.Equal(1, draft.Count);
        draft.Stage(infoFields[ClubField.YearFounded], 1889);
        Assert.Equal(0, draft.Count);

        // Stage EmbargoStart, BackgroundColour, and FirstTeam PassingStyle.
        draft.Stage(financeFields[ClubField.EmbargoStart], 20260801);
        draft.Stage(infoFields[ClubField.BackgroundColour], 0xFFFFFFFF);
        draft.Stage(firstTactical, 75);

        // Switch team to U21 (PassingStyle = 62) and stage U21 PassingStyle = 80 without losing FirstTeam edit.
        var u21Values = access.ReadAll(ClubAddr, U21TeamAddr);
        draft.AbsorbSnapshot(ClubAddr, U21TeamAddr, profile, u21Values);
        var u21Tactical = Assert.Single(ClubSectionLayout.BuildGroups(ClubSection.TacticalUnderstanding, profile, access, ClubAddr, U21TeamAddr, u21Values))
            .Fields.Single();
        Assert.Equal(62, draft.Value(u21Tactical.Target));
        draft.Stage(u21Tactical, 80);

        Assert.Equal(4, draft.Count);
        Assert.Equal(0, m.Writes);
        Assert.Equal(0, edits.ExecuteCalls);

        int saved = draft.Save(edits, access);

        Assert.Equal(4, saved);
        Assert.Equal(0, draft.Count);
        Assert.Equal(4, edits.ExecuteCalls);
        Assert.Equal(4, m.Writes);

        // 2026-08-01 = day 213 (0x00D5), year 2026 (0x07EA); 0xFFFFFFFF = FF FF FF FF.
        Assert.Equal(new byte[] { 0xD5, 0x00, 0xEA, 0x07 }, m.Read(DetailAddr + 0xE4, 4));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, m.Read(DetailAddr + 0xEC, 4));
        Assert.Equal(75, m.Read(FirstTeamAddr + 0x80, 1)[0]);
        Assert.Equal(80, m.Read(U21TeamAddr + 0x80, 1)[0]);
    }

    [Fact]
    public void Stale_game_change_stops_partial_save_and_rebase_preserves_remaining_edit()
    {
        var (m, edits) = CreateSheffieldMemory();
        var profile = ProfileWithFields("""
            "YearFounded": { "root": "club", "path": "0x100->0xD8", "type": "int32" },
            "ChairmanStatus": { "root": "club", "path": "0x100->0xDE", "type": "int16" }
            """);
        var access = new ClubFieldAccess(m, profile, TestWorld.ModuleBase);
        var values = access.ReadAll(ClubAddr, FirstTeamAddr);
        var draft = new ClubDraft(ClubAddr);
        draft.AbsorbSnapshot(ClubAddr, FirstTeamAddr, profile, values);

        var fields = Assert.Single(ClubSectionLayout.BuildGroups(ClubSection.Information, profile, access, ClubAddr, FirstTeamAddr, values))
            .Fields.ToDictionary(f => f.Field);

        draft.Stage(fields[ClubField.YearFounded], 1900);
        draft.Stage(fields[ClubField.ChairmanStatus], 7500);

        // Game mutates ChairmanStatus from 6916 to 7000 before Save.
        Assert.True(m.TryWrite(DetailAddr + 0xDE, BitConverter.GetBytes((short)7000)));

        Assert.Throws<StaleObjectException>(() => draft.Save(edits, access));

        // YearFounded (1900) wrote; ChairmanStatus in RAM kept the game's 7000; 1 pending change remains.
        Assert.Equal(1900, BitConverter.ToInt32(m.Read(DetailAddr + 0xD8, 4)));
        Assert.Equal(7000, BitConverter.ToInt16(m.Read(DetailAddr + 0xDE, 2)));
        Assert.Equal(1, draft.Count);

        draft.Rebase(ClubAddr, FirstTeamAddr, profile, access.ReadAll(ClubAddr, FirstTeamAddr));
        Assert.Equal(1, draft.Count);
        Assert.Equal(7500, draft.Value(fields[ClubField.ChairmanStatus].Target));

        Assert.Equal(1, draft.Save(edits, access));
        Assert.Equal(7500, BitConverter.ToInt16(m.Read(DetailAddr + 0xDE, 2)));
        Assert.Equal(0, draft.Count);
    }

    [Fact]
    public void Invalid_input_blocks_save_before_any_memory_write()
    {
        var (m, edits) = CreateSheffieldMemory();
        var profile = ProfileWithFields("""
            "YearFounded": { "root": "club", "path": "0x100->0xD8", "type": "int32" },
            "EmbargoStart": { "root": "club", "path": "0x100->0xE4", "type": "date" }
            """);
        var access = new ClubFieldAccess(m, profile, TestWorld.ModuleBase);
        var values = access.ReadAll(ClubAddr, FirstTeamAddr);
        var draft = new ClubDraft(ClubAddr);
        draft.AbsorbSnapshot(ClubAddr, FirstTeamAddr, profile, values);

        var yearField = Assert.Single(ClubSectionLayout.BuildGroups(ClubSection.Information, profile, access, ClubAddr, FirstTeamAddr, values)).Fields.Single();
        var dateField = Assert.Single(ClubSectionLayout.BuildGroups(ClubSection.Finances, profile, access, ClubAddr, FirstTeamAddr, values)).Fields.Single();

        draft.Stage(yearField, 1900);
        draft.SetInvalid(dateField.Target, true);

        Assert.False(draft.CanSave);
        Assert.Throws<FormatException>(() => draft.Save(edits, access));
        Assert.Equal(0, m.Writes);
        Assert.Equal(1889, BitConverter.ToInt32(m.Read(DetailAddr + 0xD8, 4)));
    }

    [Fact]
    public void Embargo_and_club_dropdown_codes_format_known_names_and_preserve_unknown_numbers()
    {
        // Known codes measured on Sheffield/Everton (0), Ismaily 106757 (4), Oxford 695 (16).
        Assert.Equal("None", ClubFieldInfo.FormatEmbargoReason(0));
        Assert.Equal("Transfer Ban", ClubFieldInfo.FormatEmbargoReason(4));
        Assert.Equal("Cannot sign attached players", ClubFieldInfo.FormatEmbargoReason(16));
        Assert.Equal("None", ClubFieldInfo.FormatFieldValue(ClubField.EmbargoReason, 0));
        Assert.Equal("Transfer Ban", ClubFieldInfo.FormatFieldValue(ClubField.EmbargoReason, 4));
        Assert.Equal("Cannot sign attached players", ClubFieldInfo.FormatFieldValue(ClubField.EmbargoReason, 16));

        // Unknown codes render as plain numbers.
        Assert.Equal("1", ClubFieldInfo.FormatEmbargoReason(1));
        Assert.Equal("8", ClubFieldInfo.FormatEmbargoReason(8));
        Assert.Equal("255", ClubFieldInfo.FormatFieldValue(ClubField.EmbargoReason, 255));

        var meta = ClubFieldInfo.For(ClubField.EmbargoReason);
        Assert.Equal(
            [new ClubCodeOption(0, "None"), new ClubCodeOption(4, "Transfer Ban"), new ClubCodeOption(16, "Cannot sign attached players")],
            ClubFieldInfo.OptionsFor(meta));
        Assert.Equal(
            [new ClubCodeOption(0, "None"), new ClubCodeOption(4, "Transfer Ban"), new ClubCodeOption(8, "8"), new ClubCodeOption(16, "Cannot sign attached players")],
            ClubFieldInfo.OptionsFor(meta, 8));

        // OwnershipType: 1 Limited Liability (Sheffield/Everton/Oxford), 3 Member-Owned (Ismaily), 5 Tottenham unconfirmed full name -> shown as "5" and preserved!
        Assert.Equal("Limited Liability", ClubFieldInfo.FormatOwnershipType(1));
        Assert.Equal("Member-Owned", ClubFieldInfo.FormatOwnershipType(3));
        Assert.Equal("5", ClubFieldInfo.FormatOwnershipType(5));
        Assert.Equal("2", ClubFieldInfo.FormatOwnershipType(2));
        Assert.Equal("Limited Liability", ClubFieldInfo.FormatFieldValue(ClubField.OwnershipType, 1));
        Assert.Equal("Member-Owned", ClubFieldInfo.FormatFieldValue(ClubField.OwnershipType, 3));
        Assert.Equal("5", ClubFieldInfo.FormatFieldValue(ClubField.OwnershipType, 5));
        var ownershipMeta = ClubFieldInfo.For(ClubField.OwnershipType);
        Assert.Equal(
            [new ClubCodeOption(1, "Limited Liability"), new ClubCodeOption(3, "Member-Owned")],
            ClubFieldInfo.OptionsFor(ownershipMeta));
        Assert.Equal(
            [new ClubCodeOption(1, "Limited Liability"), new ClubCodeOption(3, "Member-Owned"), new ClubCodeOption(5, "5")],
            ClubFieldInfo.OptionsFor(ownershipMeta, 5));

        // ChairpersonTitle: 1 Chairperson (Sheffield/Everton/Oxford/PSG/Tottenham), 2 President (Ismaily).
        Assert.Equal("Chairperson", ClubFieldInfo.FormatChairpersonTitle(1));
        Assert.Equal("President", ClubFieldInfo.FormatChairpersonTitle(2));
        Assert.Equal("9", ClubFieldInfo.FormatChairpersonTitle(9));
        Assert.Equal("Chairperson", ClubFieldInfo.FormatFieldValue(ClubField.ChairpersonTitle, 1));
        Assert.Equal("President", ClubFieldInfo.FormatFieldValue(ClubField.ChairpersonTitle, 2));
        Assert.Equal("9", ClubFieldInfo.FormatFieldValue(ClubField.ChairpersonTitle, 9));
        var chairMeta = ClubFieldInfo.For(ClubField.ChairpersonTitle);
        Assert.Equal(
            [new ClubCodeOption(1, "Chairperson"), new ClubCodeOption(2, "President")],
            ClubFieldInfo.OptionsFor(chairMeta));
        Assert.Equal(
            [new ClubCodeOption(1, "Chairperson"), new ClubCodeOption(2, "President"), new ClubCodeOption(9, "9")],
            ClubFieldInfo.OptionsFor(chairMeta, 9));

        // RichBenefactor: 0 None (Tottenham/Ismaily), 1 Foreground (PSG), 3 Underwriter (Sheffield/Everton/Oxford), 4 Underwriter (Expects Returns) (Shenzhen).
        Assert.Equal("None", ClubFieldInfo.FormatRichBenefactor(0));
        Assert.Equal("Foreground", ClubFieldInfo.FormatRichBenefactor(1));
        Assert.Equal("Underwriter", ClubFieldInfo.FormatRichBenefactor(3));
        Assert.Equal("Underwriter (Expects Returns)", ClubFieldInfo.FormatRichBenefactor(4));
        Assert.Equal("2", ClubFieldInfo.FormatRichBenefactor(2));
        Assert.Equal("None", ClubFieldInfo.FormatFieldValue(ClubField.RichBenefactor, 0));
        Assert.Equal("Foreground", ClubFieldInfo.FormatFieldValue(ClubField.RichBenefactor, 1));
        Assert.Equal("Underwriter", ClubFieldInfo.FormatFieldValue(ClubField.RichBenefactor, 3));
        Assert.Equal("Underwriter (Expects Returns)", ClubFieldInfo.FormatFieldValue(ClubField.RichBenefactor, 4));
        Assert.Equal("2", ClubFieldInfo.FormatFieldValue(ClubField.RichBenefactor, 2));
        var benefactorMeta = ClubFieldInfo.For(ClubField.RichBenefactor);
        Assert.Equal(
            [
                new ClubCodeOption(0, "None"),
                new ClubCodeOption(1, "Foreground"),
                new ClubCodeOption(3, "Underwriter"),
                new ClubCodeOption(4, "Underwriter (Expects Returns)"),
            ],
            ClubFieldInfo.OptionsFor(benefactorMeta));
        Assert.Equal(
            [
                new ClubCodeOption(0, "None"),
                new ClubCodeOption(1, "Foreground"),
                new ClubCodeOption(2, "2"),
                new ClubCodeOption(3, "Underwriter"),
                new ClubCodeOption(4, "Underwriter (Expects Returns)"),
            ],
            ClubFieldInfo.OptionsFor(benefactorMeta, 2));
    }

    [Fact]
    public void Shipped_profile_renders_float_ticket_price_embargo_codes_dates_and_small_club_finances()
    {
        const ulong HolderAddr = 0x5005_0000;
        const uint FullFinanceVt = 0x4CF0CC8, SmallFinanceVt = 0x4CF07F8;
        var world = new TestWorld();
        var m = world.Memory;
        var profile = OffsetProfile.LoadFile(TestWorld.ProfilePath);

        var club = m.Map(ClubAddr, 0x170);
        TestWorld.Set64(club, 0x00, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(club, 0xB0, HolderAddr);
        TestWorld.Set64(club, 0x100, DetailAddr);
        TestWorld.Set64(club, 0x150, FinanceAddr);

        var holder = m.Map(HolderAddr, 0x80);
        BitConverter.GetBytes(27_676).CopyTo(holder, 0x68);
        BitConverter.GetBytes(10_000).CopyTo(holder, 0x6C);
        BitConverter.GetBytes(34_595).CopyTo(holder, 0x70);

        var detail = m.Map(DetailAddr, 0x140);
        BitConverter.GetBytes(19_725).CopyTo(detail, 0xC0);
        BitConverter.GetBytes((short)1889).CopyTo(detail, 0xD8);
        BitConverter.GetBytes((short)6916).CopyTo(detail, 0xDE);
        BitConverter.GetBytes(-1).CopyTo(detail, 0xF8); // Oxford 695's -1 ("none")
        new byte[] { 12, 40, 15, 17, 2, 14 }.CopyTo(detail, 0x106);
        detail[0x116] = 6;
        detail[0x119] = 10;
        detail[0x120] = 13;
        detail[0x126] = 14;
        detail[0x12B] = 12;
        detail[0x12C] = 13;
        detail[0x12D] = 15;

        var finance = m.Map(FinanceAddr, 0x8B0);
        TestWorld.Set64(finance, 0x00, TestWorld.ModuleBase + FullFinanceVt);
        BitConverter.GetBytes(5_790_291).CopyTo(finance, 0x0C);
        BitConverter.GetBytes(35.0f).CopyTo(finance, 0x10); // Sheffield £35 float (00 00 0C 42)
        Convert.FromHexString("01006C07").CopyTo(finance, 0x28); // 1/1/1900 -> 0 -> ""
        Convert.FromHexString("6D01EA07").CopyTo(finance, 0x2C); // day 365, year 2026 -> 2026-12-31
        finance[0x30] = 15;
        finance[0x33] = 0;
        BitConverter.GetBytes(6_000_000).CopyTo(finance, 0x7C4);
        BitConverter.GetBytes(6_000_000).CopyTo(finance, 0x7C8);
        BitConverter.GetBytes(496_589).CopyTo(finance, 0x808);
        BitConverter.GetBytes(25_000).CopyTo(finance, 0x80C);
        BitConverter.GetBytes(490_710).CopyTo(finance, 0x814);

        var firstTeam = m.Map(FirstTeamAddr, 0x100);
        TestWorld.Set64(firstTeam, 0x00, TestWorld.ModuleBase + TeamVt);
        BitConverter.GetBytes((short)6200).CopyTo(firstTeam, 0xA8);

        var access = new ClubFieldAccess(m, profile, TestWorld.ModuleBase);
        var values = access.ReadAll(ClubAddr, FirstTeamAddr);
        Assert.True(values.Count >= 32, $"Expected >= 32 readable fields, got {values.Count}");

        var allGroups = ClubFieldInfo.Sections
            .SelectMany(s => ClubSectionLayout.BuildGroups(s, profile, access, ClubAddr, FirstTeamAddr, values))
            .ToArray();
        Assert.Equal(values.Count, allGroups.Sum(g => g.Fields.Count));

        var infoGroups = ClubSectionLayout.BuildGroups(ClubSection.Information, profile, access, ClubAddr, FirstTeamAddr, values);
        var financeGroups = ClubSectionLayout.BuildGroups(ClubSection.Finances, profile, access, ClubAddr, FirstTeamAddr, values);

        // Balance is first in Finances / Overview; SocialMediaFollowers accepts -1.
        Assert.Equal(ClubField.Balance, financeGroups.First(g => g.Title == "Overview").Fields[0].Field);
        Assert.Equal("5,790,291", ClubFieldInfo.FormatFieldValue(ClubField.Balance, values[ClubField.Balance]));
        Assert.Equal("-31,697", ClubFieldInfo.FormatFieldValue(ClubField.Balance, -31_697));
        var followersField = infoGroups.SelectMany(g => g.Fields).Single(f => f.Field == ClubField.SocialMediaFollowers);
        Assert.Equal(-1, values[ClubField.SocialMediaFollowers]);
        Assert.True(followersField.Min <= -1);
        Assert.Equal("-1", ClubFieldInfo.FormatValue(values[ClubField.SocialMediaFollowers], followersField.Kind));

        // AverageTicketPrice is float in RAM (35.0f) but displayed and edited as an integer (35).
        var ticketField = financeGroups.SelectMany(g => g.Fields).Single(f => f.Field == ClubField.AverageTicketPrice);
        Assert.Equal(ClubDisplayKind.FormattedInt, ticketField.Kind);
        Assert.Equal((-16_777_216L, 16_777_216L), ClubFieldInfo.TypeBounds(ticketField.Offset));
        Assert.Equal(35, values[ClubField.AverageTicketPrice]);
        Assert.Equal("35", ClubFieldInfo.FormatValue(values[ClubField.AverageTicketPrice], ticketField.Kind));

        // Embargo dates show "" for 0 (1/1/1900) and yyyy-MM-dd for real dates; EmbargoReason shows mapped name.
        var embargoEnd = financeGroups.SelectMany(g => g.Fields).Single(f => f.Field == ClubField.EmbargoEnd);
        var embargoAppeal = financeGroups.SelectMany(g => g.Fields).Single(f => f.Field == ClubField.EmbargoAppealDate);
        var embargoReason = financeGroups.SelectMany(g => g.Fields).Single(f => f.Field == ClubField.EmbargoReason);
        Assert.Equal(0, values[ClubField.EmbargoEnd]);
        Assert.Equal("", ClubFieldInfo.FormatValue(values[ClubField.EmbargoEnd], embargoEnd.Kind));
        Assert.Equal(20261231, values[ClubField.EmbargoAppealDate]);
        Assert.Equal("2026-12-31", ClubFieldInfo.FormatValue(values[ClubField.EmbargoAppealDate], embargoAppeal.Kind));
        Assert.Equal("None", ClubFieldInfo.FormatFieldValue(ClubField.EmbargoReason, values[ClubField.EmbargoReason]));

        // Editing AverageTicketPrice (35 -> 57, Everton £57), EmbargoReason (0 -> 16, Oxford), and clearing AppealDate ("" -> 0).
        var repo = new PlayerRepository(m, profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(m, profile, TestWorld.ModuleBase);
        var edits = new RecordingEditService(new EditService(writer, repo));
        var draft = new ClubDraft(ClubAddr);
        draft.AbsorbSnapshot(ClubAddr, FirstTeamAddr, profile, values);

        Assert.True(ClubFieldInfo.TryParseDate("", out long clearedDate) && clearedDate == 0);
        draft.Stage(ticketField, 57);
        draft.Stage(embargoReason, 16);
        draft.Stage(embargoAppeal, clearedDate);

        Assert.Equal(3, draft.Save(edits, access));
        Assert.Equal(3, edits.ExecuteCalls);
        // 57.0f IEEE-754 little-endian = 0x42640000 -> 00 00 64 42.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x64, 0x42 }, m.Read(FinanceAddr + 0x10, 4));
        Assert.Equal(16, m.Read(FinanceAddr + 0x33, 1)[0]);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x6C, 0x07 }, m.Read(FinanceAddr + 0x2C, 4));
        Assert.Equal(0, access.Read(ClubAddr, ClubField.EmbargoAppealDate));

        // Small-club finance vtable (Oxford 695, 0x4CF07F8): unstored budget/wage/ticket rows are omitted (never shown as 0), Balance + embargo stay.
        TestWorld.Set64(finance, 0x00, TestWorld.ModuleBase + SmallFinanceVt);
        var smallValues = access.ReadAll(ClubAddr, FirstTeamAddr);
        var smallFinanceGroups = ClubSectionLayout.BuildGroups(ClubSection.Finances, profile, access, ClubAddr, FirstTeamAddr, smallValues);
        var smallOverviewFields = smallFinanceGroups.First(g => g.Title == "Overview").Fields.Select(f => f.Field).ToArray();
        Assert.Contains(ClubField.Balance, smallOverviewFields);
        foreach (var unstored in new[]
        {
            ClubField.TransferBudgetSeason,
            ClubField.TransferBudgetRemaining,
            ClubField.WageBudget,
            ClubField.WageUsed,
            ClubField.MaxWage,
            ClubField.AverageTicketPrice,
        })
        {
            Assert.DoesNotContain(unstored, smallValues.Keys);
            Assert.DoesNotContain(unstored, smallOverviewFields);
        }
        var smallEmbargoFields = smallFinanceGroups.First(g => g.Title == "Transfer Embargo").Fields.Select(f => f.Field).ToArray();
        Assert.Contains(ClubField.EmbargoReason, smallEmbargoFields);
        Assert.Contains(ClubField.EmbargoEnd, smallEmbargoFields);

        // Null finance pointer: entire Finances section is omitted rather than rendering 0s.
        TestWorld.Set64(club, 0x150, 0);
        var noFinanceValues = access.ReadAll(ClubAddr, FirstTeamAddr);
        Assert.Empty(ClubSectionLayout.BuildGroups(ClubSection.Finances, profile, access, ClubAddr, FirstTeamAddr, noFinanceValues));
    }

    [Fact]
    public void Priority_club_lists_render_vision_affiliations_and_regional_divisions_matching_sheffield_and_everton_oracles()
    {
        // 1. Club Vision (confirmed-only Everton 650 rows from STATUS [A 21:40] & re/club/lists.md):
        // Candidate Source/ForCompetition/Value (-1 sentinel) stay omitted rather than showing 0 or False.
        var evertonVision = ClubListBuilder.BuildVision(
        [
            new ClubVisionRow(120, 3, StartDate: new DateOnly(1900, 1, 1), EndDate: new DateOnly(1900, 1, 1), Value: -1),
            new ClubVisionRow(40, 5, StartDate: new DateOnly(2026, 7, 13), EndDate: new DateOnly(2031, 7, 17)),
            new ClubVisionRow(182, 6, StartDate: new DateOnly(2026, 7, 13), EndDate: new DateOnly(1900, 1, 1), ItemName: "Liverpool"),
        ]);

        Assert.Equal(["Type", "Importance", "Item", "Start", "End"], evertonVision.Columns.Select(c => c.Header));
        Assert.Equal(
            ["Develop players using the club's youth system", "3", "", "", ""],
            evertonVision.Rows[0].Cells.Select(c => c.Text));
        Assert.DoesNotContain(evertonVision.Rows[0].Details, d => d.Label is "Source" or "For Competition" or "Value");
        Assert.Equal(
            ["Become self-sustainable", "5", "", "2026-07-13", "2031-07-17"],
            evertonVision.Rows[1].Cells.Select(c => c.Text));
        Assert.Equal(
            ["Unknown (182)", "6", "Liverpool", "2026-07-13", ""],
            evertonVision.Rows[2].Cells.Select(c => c.Text));
        Assert.Equal("Unknown (182), 6, Liverpool, 2026-07-13", evertonVision.Rows[2].AutomationName);
        Assert.Equal(
            [
                ("Type", "Unknown (182)"),
                ("Importance", "6"),
                ("Item", "Liverpool"),
                ("Start Date", "2026-07-13"),
                ("End Date", ""),
            ],
            evertonVision.Rows[2].Details.Select(d => (d.Label, d.Value)));

        // Full Sheffield 708 rows (docs/club-plan.md): Source, For Comp., and Value (age 23) surface automatically.
        var sheffieldVision = ClubListBuilder.BuildVision(
        [
            new ClubVisionRow(3, 2, new DateOnly(2026, 7, 13), new DateOnly(2029, 5, 4), ItemName: "Premier League", SourceCode: 0, ForCompetition: true, Value: 2),
            new ClubVisionRow(104, 4, SourceCode: 0, ForCompetition: false, Value: 23),
        ]);
        Assert.Equal(
            ["Type", "Source", "Importance", "Item", "For Comp.", "Start", "End"],
            sheffieldVision.Columns.Select(c => c.Header));
        Assert.Equal(
            ["Win promotion", "Board", "2", "Premier League", "True", "2026-07-13", "2029-05-04"],
            sheffieldVision.Rows[0].Cells.Select(c => c.Text));
        Assert.Contains(
            sheffieldVision.Rows[1].Details,
            d => d.Label == "Type" && d.Value == "Sign players under the age of X");
        Assert.Contains(
            sheffieldVision.Rows[1].Details,
            d => d.Label == "Value" && d.Value == "23");

        // 2. Affiliations (Everton 650 confirmed rows + Sheffield 708 unresolved feeder name fallback to ID):
        var affiliations = ClubListBuilder.BuildAffiliations(
        [
            new ClubAffiliationRow("Everton", "Everton de Viña del Mar", 16, new DateOnly(2010, 5, 1), ParentClubId: 650, FeederClubId: 130782),
            new ClubAffiliationRow("Sheffield United", null, 25, new DateOnly(2024, 4, 17), ParentClubId: 708, FeederClubId: 2000108253),
        ]);
        Assert.Equal(["Parent Club", "Feeder Club", "Type", "Start"], affiliations.Columns.Select(c => c.Header));
        Assert.Equal(
            ["Everton", "Everton de Viña del Mar", "Good Relations", "2010-05-01"],
            affiliations.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(
            ["Sheffield United", "2000108253", "Women's Club", "2024-04-17"],
            affiliations.Rows[1].Cells.Select(c => c.Text));
        Assert.DoesNotContain(affiliations.Rows[0].Details, d => d.Label is "Annual Fee" or "End Date" or "Benefits Mask");

        // 3. Regional Divisions: hides Division column when all rows have empty/null DivisionName (Sheffield 708 6/7/8, Everton 650 8 rows),
        // and includes Division column when at least one row has a non-empty DivisionName.
        var levelOnlyDivisions = ClubListBuilder.BuildRegionalDivisions(
        [
            new RegionalDivisionRow(6),
            new RegionalDivisionRow(7),
            new RegionalDivisionRow(8),
        ]);
        Assert.Equal(["Level"], levelOnlyDivisions.Columns.Select(c => c.Header));
        Assert.Equal(["6", "7", "8"], levelOnlyDivisions.Rows.Select(r => Assert.Single(r.Cells).Text));
        Assert.Equal([("Level", "6")], levelOnlyDivisions.Rows[0].Details.Select(d => (d.Label, d.Value)));

        var divisions = ClubListBuilder.BuildRegionalDivisions(
        [
            new RegionalDivisionRow(6, DivisionName: "Enterprise National League North", CompetitionId: 5_123_054),
            new RegionalDivisionRow(1, DivisionName: null, CompetitionId: 95_041_291),
        ]);
        Assert.Equal(["Division", "ID", "Level"], divisions.Columns.Select(c => c.Header));
        Assert.Equal(["Enterprise National League North", "5123054", "6"], divisions.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(["", "95041291", "1"], divisions.Rows[1].Cells.Select(c => c.Text));
        Assert.Equal([("Competition ID", "95041291"), ("Level", "1")], divisions.Rows[1].Details.Select(d => (d.Label, d.Value)));
    }

    [Fact]
    public void Secondary_club_lists_render_sponsors_debts_kits_and_transfer_center_matching_sheffield_and_everton_oracles()
    {
        // 1. Sponsors (Sheffield + Everton FMRTE: type 3 is Stadium Sponsor).
        var sponsors = ClubListBuilder.BuildSponsors(
        [
            new ClubSponsorRow(4, 10_591_455, new DateOnly(2024, 11, 30), new DateOnly(2027, 12, 1), AnnualValue: 3_530_485),
            new ClubSponsorRow(3, 35_000_000, new DateOnly(2025, 5, 15), new DateOnly(2030, 6, 30)),
        ]);
        Assert.Equal(["Income Type", "Total Value", "Start", "End"], sponsors.Columns.Select(c => c.Header));
        Assert.Equal(
            ["General Sponsor", "10,591,455", "2024-11-30", "2027-12-01"],
            sponsors.Rows[0].Cells.Select(c => c.Text));
        Assert.Contains(sponsors.Rows[0].Details, d => d.Label == "Annual Value" && d.Value == "3,530,485");
        Assert.Equal("Stadium Sponsor", sponsors.Rows[1].Cells[0].Text);
        Assert.Equal("14", ClubListLabels.SponsorIncomeType(14));
        Assert.DoesNotContain(sponsors.Rows[1].Details, d => d.Label == "Annual Value");

        // 2. Debt and Loans (Sheffield + Everton FMRTE: source 15 is Miscellaneous).
        var debts = ClubListBuilder.BuildDebts(
        [
            new ClubDebtRow(10, 11_000_000, new DateOnly(2025, 7, 1), new DateOnly(2028, 6, 30)),
            new ClubDebtRow(1, 23_954_758, new DateOnly(2025, 7, 1), new DateOnly(2035, 6, 30)),
            new ClubDebtRow(15, 36_700_000, new DateOnly(2014, 5, 1), new DateOnly(2057, 6, 30)),
        ]);
        Assert.Equal(
            ["Loan Repayable On Promotion To Top Division", "11,000,000", "2025-07-01", "2028-06-30"],
            debts.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal("Bank", debts.Rows[1].Cells[0].Text);
        Assert.Equal("Miscellaneous", debts.Rows[2].Cells[0].Text);
        Assert.Equal("14", ClubListLabels.DebtSource(14));
        Assert.DoesNotContain(debts.Rows[0].Details, d => d.Label == "Instalment");

        // 3. Kits and Colours (Sheffield Home shirt raw BGRA bytes: [30, 20, B0, FF] -> uint32 0xFFB02030 = #B02030 red):
        uint claret = BitConverter.ToUInt32([0x30, 0x20, 0xB0, 0xFF]);
        uint white = BitConverter.ToUInt32([0xF8, 0xF8, 0xF8, 0xFF]);
        uint black = BitConverter.ToUInt32([0x08, 0x08, 0x08, 0xFF]);
        var kits = ClubListBuilder.BuildKits(
        [
            new ClubKitRow(claret, white, black, white, claret, Style: 3),
        ]);
        Assert.Equal(["Foreground", "Background", "Outline", "Style"], kits.Columns.Select(c => c.Header));
        Assert.Equal(["#B02030", "#F8F8F8", "#080808", "3"], kits.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(((byte)0xFF, (byte)0xB0, (byte)0x20, (byte)0x30), ClubFieldInfo.SwatchArgb(kits.Rows[0].Cells[0].SwatchArgb!.Value));
        Assert.Equal(
            [
                ("Foreground", "#B02030"),
                ("Background", "#F8F8F8"),
                ("Outline", "#080808"),
                ("Number", "#F8F8F8"),
                ("Number Outline", "#B02030"),
                ("Item Style", "3"),
            ],
            kits.Rows[0].Details.Select(d => (d.Label, d.Value)));

        // 4. Transfer Center (Sheffield Steve Brown null->Sheffield £0, Liam McLaughlin Sheffield->Burnley £130,050, plus unresolved subclass row):
        var transfers = ClubListBuilder.BuildTransferCenter(
        [
            new ClubTransferOfferRow(832, "Steve Brown", 28_111_325, SellerClub: null, BuyerClub: "Sheffield United", BuyerClubId: 708, Fee: 0, StatusCode: 8),
            new ClubTransferOfferRow(907, "Liam McLaughlin", 2_000_328_349, SellerClub: "Sheffield United", SellerClubId: 708, BuyerClub: "Burnley", BuyerClubId: 622, Fee: 130_050, StatusCode: 8),
            new ClubTransferOfferRow(1097, "Braiden Graham", 2_000_252_560, SellerClub: "Everton", BuyerClub: "MK Dons", Fee: null, StatusCode: null),
        ]);
        Assert.Equal(["Person", "From", "To", "Fee", "Status"], transfers.Columns.Select(c => c.Header));
        Assert.Equal(["Steve Brown", "", "Sheffield United", "0", "Player accepted"], transfers.Rows[0].Cells.Select(c => c.Text));
        Assert.DoesNotContain(transfers.Rows[0].Details, d => d.Label == "From");
        Assert.Equal(
            ["Liam McLaughlin", "Sheffield United", "Burnley", "130,050", "Player accepted"],
            transfers.Rows[1].Cells.Select(c => c.Text));
        Assert.Equal(["Braiden Graham", "Everton", "MK Dons", "", ""], transfers.Rows[2].Cells.Select(c => c.Text));
        Assert.DoesNotContain(transfers.Rows[2].Details, d => d.Label is "Fee" or "Status");
    }

    [Fact]
    public void Transfer_center_shows_offer_type_and_FMRTE_status()
    {
        var table = ClubListBuilder.BuildTransferCenter([
            new ClubTransferOfferRow(5624, "Alejandro Balde", BuyerClub: "Manchester United", SellerClub: "Barcelona", Fee: 150000, StatusCode: 12, TypeName: "Loan"),
            new ClubTransferOfferRow(4062, "Julio Figueroa", BuyerClub: "Manchester United", Fee: 0, StatusCode: 10, TypeName: "Transfer"),
        ]);
        Assert.Equal(["Type", "Person", "From", "To", "Fee", "Status"], table.Columns.Select(c => c.Header));
        Assert.Equal(["Loan", "Alejandro Balde", "Barcelona", "Manchester United", "150,000", "Ready to sign"], table.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(["Transfer", "Julio Figueroa", "", "Manchester United", "0", "Unknown status 1"], table.Rows[1].Cells.Select(c => c.Text));
    }

    sealed record StubCoreVisionItem(
        ulong Address,
        byte Type,
        byte Importance,
        DateOnly? Start,
        DateOnly? End,
        ClubInfo? ClubItem);

    sealed record StubCoreAffiliation(
        ulong Address,
        ClubInfo? Parent,
        ClubInfo? Feeder,
        DateOnly? Start,
        byte Type);

    sealed record StubCoreRegionalDivision(
        int CompetitionId,
        CompetitionInfo? Competition,
        byte Level);

    sealed class StubSessionWithoutListReaders
    {
        public IReadOnlyList<TransferClause> ReadTransferClauses(ulong clubAddress) =>
            throw new InvalidOperationException("Must not treat ReadTransferClauses as a Club list section reader");
    }

    sealed class StubSessionWithPriorityReaders
    {
        public IReadOnlyList<StubCoreVisionItem> ReadClubVision(ulong teamAddress, IReadOnlyList<ClubEntry> clubs) =>
            teamAddress == FirstTeamAddr
                ?
                [
                    new(0x1AB5_5296_720, 44, 10, new DateOnly(2026, 7, 13), null, null),
                    new(0x1AB5_5296_770, 182, 6, new DateOnly(2026, 7, 13), new DateOnly(1900, 1, 1), new ClubInfo(0x1AB6_91E5_2E0, "Liverpool", "Liverpool")),
                ]
                :
                [
                    new(0x1AB5_5296_7C0, 120, 4, null, null, null),
                ];

        public IReadOnlyList<StubCoreAffiliation> ReadAffiliations(ulong clubAddress) =>
            clubAddress == ClubAddr
                ? [new(0x1AB6_32D5_5E0, new ClubInfo(ClubAddr, "Everton", "Everton"), new ClubInfo(0x1AB6_3200_000, "Sligo Rovers FC", "Sligo Rovers"), new DateOnly(2021, 1, 12), 16)]
                : [];

        public IReadOnlyList<StubCoreRegionalDivision> ReadRegionalDivisions(ulong clubAddress, IReadOnlyDictionary<int, CompetitionInfo> competitions) =>
        [
            new(5_123_054, competitions.GetValueOrDefault(8), 6),
        ];

        public IReadOnlyList<ClubSponsorEntry> ReadSponsors(ulong clubAddress) =>
            clubAddress == ClubAddr
                ?
                [
                    // Sheffield United (ID 708) 13 confirmed sponsors from FMRTE 27 (docs/club-plan.md & [A 23:20]):
                    new(0x1001, 4, 10_591_455, new DateOnly(2024, 11, 30), new DateOnly(2027, 12, 1)),
                    new(0x1002, 22, 6_000_000, new DateOnly(2022, 6, 17), new DateOnly(2026, 7, 1)),
                    new(0x1003, 1, 7_500_000, new DateOnly(2024, 7, 1), new DateOnly(2027, 6, 30)),
                    new(0x1004, 18, 2_774_972, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30)),
                    new(0x1005, 4, 294_576, new DateOnly(2026, 5, 29), new DateOnly(2027, 5, 29)),
                    new(0x1006, 4, 1_193_979, new DateOnly(2024, 6, 20), new DateOnly(2027, 6, 20)),
                    new(0x1007, 4, 591_627, new DateOnly(2025, 5, 19), new DateOnly(2028, 5, 19)),
                    new(0x1008, 4, 381_741, new DateOnly(2024, 6, 6), new DateOnly(2026, 6, 6)),
                    new(0x1009, 4, 520_644, new DateOnly(2025, 6, 30), new DateOnly(2027, 6, 30)),
                    new(0x100A, 19, 1_055_040, new DateOnly(2025, 6, 11), new DateOnly(2028, 6, 11)),
                    new(0x100B, 11, 610_426, new DateOnly(2025, 6, 20), new DateOnly(2027, 6, 20)),
                    new(0x100C, 23, 2_745_488, new DateOnly(2025, 6, 19), new DateOnly(2029, 6, 19)),
                    new(0x100D, 4, 521_267, new DateOnly(2025, 6, 16), new DateOnly(2027, 6, 16)),
                ]
                :
                [
                    // Oxford United (ID 695) unknown income type codes 14, 15, 9, 12 ([A 23:20]):
                    new(0x2001, 14, 100_000, new DateOnly(2025, 7, 1), new DateOnly(2027, 6, 30)),
                    new(0x2002, 15, 80_000, new DateOnly(2025, 7, 1), new DateOnly(2027, 6, 30)),
                    new(0x2003, 9, 50_000, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30)),
                    new(0x2004, 12, 40_000, new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30)),
                ];

        public IReadOnlyList<ClubDebtEntry> ReadDebts(ulong clubAddress) =>
            clubAddress == ClubAddr
                ?
                [
                    // Sheffield United (ID 708) 3 confirmed debts from FMRTE 27 (docs/club-plan.md & [A 23:20]):
                    new(0x3001, 10, 11_000_000, new DateOnly(2025, 7, 1), new DateOnly(2028, 6, 30)),
                    new(0x3002, 1, 23_954_758, new DateOnly(2025, 7, 1), new DateOnly(2035, 6, 30)),
                    new(0x3003, 1, 28_000_000, new DateOnly(2020, 9, 1), new DateOnly(2045, 7, 1)),
                ]
                : [];
    }

    [Fact]
    public void List_provider_stays_empty_without_core_readers_and_maps_core_records_when_readers_exist()
    {
        var provider = GameSessionClubListProvider.Default;
        var firstTeam = new TeamEntry(FirstTeamAddr, ClubAddr, 0, 25, 30, IsFirstTeam: true);
        var u21Team = new TeamEntry(U21TeamAddr, ClubAddr, 0, 18, 25, IsFirstTeam: false);
        var everton = new ClubEntry(ClubAddr, 650, "Everton", "Everton", FinanceAddr, [firstTeam, u21Team]);
        var oxford = new ClubEntry(0x5009_0000, 695, "Oxford United", "Oxford", FinanceAddr, []);
        var competitions = new Dictionary<int, CompetitionInfo>
        {
            [8] = new(0x1AB5_6160_E00, "Enterprise National League North", "National League N"),
        };

        // 1. Without Core list readers, every list section reports HasReader = false and empty rows (never calling ReadTransferClauses).
        var noReaderSession = new StubSessionWithoutListReaders();
        foreach (var section in ClubFieldInfo.ListSections)
        {
            Assert.False(provider.HasReader(noReaderSession, section));
            var emptyTable = provider.ReadSection(section, noReaderSession, everton, firstTeam, [everton], competitions, []);
            Assert.False(emptyTable.HasReader);
            Assert.Empty(emptyTable.Rows);
        }

        // 2. When Core exposes priority list readers + ReadSponsors/ReadDebts, provider binds club/team/competitions and maps Core records without leaking RAM addresses.
        var coreSession = new StubSessionWithPriorityReaders();
        Assert.True(provider.HasReader(coreSession, ClubSection.ClubVision));
        Assert.True(provider.HasReader(coreSession, ClubSection.Affiliations));
        Assert.True(provider.HasReader(coreSession, ClubSection.RegionalDivisions));
        Assert.True(provider.HasReader(coreSession, ClubSection.Sponsors));
        Assert.True(provider.HasReader(coreSession, ClubSection.Debts));
        Assert.False(provider.HasReader(coreSession, ClubSection.Kits));
        Assert.False(provider.NeedsCompetitions(coreSession, ClubSection.Affiliations));
        Assert.True(provider.NeedsCompetitions(coreSession, ClubSection.RegionalDivisions));

        var firstVision = provider.ReadSection(ClubSection.ClubVision, coreSession, everton, firstTeam, [everton], competitions, []);
        Assert.True(firstVision.HasReader);
        Assert.True(firstVision.UsesTeamRoot);
        Assert.Equal(2, firstVision.Rows.Count);
        Assert.Equal(["Work within wage budget", "10", "", "2026-07-13", ""], firstVision.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(["Unknown (182)", "6", "Liverpool", "2026-07-13", ""], firstVision.Rows[1].Cells.Select(c => c.Text));
        Assert.DoesNotContain("1AB5", firstVision.Rows[1].AutomationName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StubCore", firstVision.Rows[1].ToString(), StringComparison.Ordinal);

        var u21Vision = provider.ReadSection(ClubSection.ClubVision, coreSession, everton, u21Team, [everton], competitions, []);
        Assert.Equal(
            ["Develop players using the club's youth system", "4", "", "", ""],
            Assert.Single(u21Vision.Rows).Cells.Select(c => c.Text));

        var affiliations = provider.ReadSection(ClubSection.Affiliations, coreSession, everton, firstTeam, [everton], competitions, []);
        Assert.False(affiliations.UsesTeamRoot);
        Assert.Equal(
            ["Everton", "Sligo Rovers FC", "Good Relations", "2021-01-12"],
            Assert.Single(affiliations.Rows).Cells.Select(c => c.Text));

        var regional = provider.ReadSection(ClubSection.RegionalDivisions, coreSession, everton, firstTeam, [everton], competitions, []);
        Assert.Equal(
            ["Enterprise National League North", "5123054", "6"],
            Assert.Single(regional.Rows).Cells.Select(c => c.Text));

        // 3. Sheffield 13 sponsors + 3 debts via Core's ClubSponsorEntry and ClubDebtEntry records:
        var sheffieldSponsors = provider.ReadSection(ClubSection.Sponsors, coreSession, everton, firstTeam, [everton], competitions, []);
        Assert.True(sheffieldSponsors.HasReader);
        Assert.False(sheffieldSponsors.UsesTeamRoot);
        Assert.Equal(13, sheffieldSponsors.Rows.Count);
        Assert.Equal(["General Sponsor", "10,591,455", "2024-11-30", "2027-12-01"], sheffieldSponsors.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(["Kit Manufacturer", "6,000,000", "2022-06-17", "2026-07-01"], sheffieldSponsors.Rows[1].Cells.Select(c => c.Text));
        Assert.Equal(["Kit Sponsor", "7,500,000", "2024-07-01", "2027-06-30"], sheffieldSponsors.Rows[2].Cells.Select(c => c.Text));
        Assert.Equal(["Total Commercial Income", "2,774,972", "2025-07-01", "2026-06-30"], sheffieldSponsors.Rows[3].Cells.Select(c => c.Text));
        Assert.Equal(["Shirt Sleeve", "1,055,040", "2025-06-11", "2028-06-11"], sheffieldSponsors.Rows[9].Cells.Select(c => c.Text));
        Assert.Equal(["Back of Shirt", "610,426", "2025-06-20", "2027-06-20"], sheffieldSponsors.Rows[10].Cells.Select(c => c.Text));
        Assert.Equal(["Stadium", "2,745,488", "2025-06-19", "2029-06-19"], sheffieldSponsors.Rows[11].Cells.Select(c => c.Text));
        Assert.Equal(["General Sponsor", "521,267", "2025-06-16", "2027-06-16"], sheffieldSponsors.Rows[12].Cells.Select(c => c.Text));

        var sheffieldDebts = provider.ReadSection(ClubSection.Debts, coreSession, everton, firstTeam, [everton], competitions, []);
        Assert.True(sheffieldDebts.HasReader);
        Assert.False(sheffieldDebts.UsesTeamRoot);
        Assert.Equal(3, sheffieldDebts.Rows.Count);
        Assert.Equal(["Loan Repayable On Promotion To Top Division", "11,000,000", "2025-07-01", "2028-06-30"], sheffieldDebts.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(["Bank", "23,954,758", "2025-07-01", "2035-06-30"], sheffieldDebts.Rows[1].Cells.Select(c => c.Text));
        Assert.Equal(["Bank", "28,000,000", "2020-09-01", "2045-07-01"], sheffieldDebts.Rows[2].Cells.Select(c => c.Text));

        // 4. Oxford unknown sponsor income type codes (14, 15, 9, 12) render as plain numbers:
        var oxfordSponsors = provider.ReadSection(ClubSection.Sponsors, coreSession, oxford, null, [oxford], competitions, []);
        Assert.Equal(["14", "15", "9", "12"], oxfordSponsors.Rows.Select(r => r.Cells[0].Text));
    }
}
