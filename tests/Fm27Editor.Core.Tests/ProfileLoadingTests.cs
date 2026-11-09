using Fm27Editor.Core.Game;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Tests;

public class ProfileLoadingTests
{
    [Fact]
    public void Game_pass_and_steam_profiles_coexist_and_game_pass_requires_its_pe_fingerprint()
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Single(profiles, p => p.Build == "27.0.0+2493426");
        var gamePass = Assert.Single(profiles, p => p.Build == "27.0.0+2493426 Game Pass");
        Assert.False(gamePass.ReadOnly);
        var roundTrip = OffsetProfile.Load(gamePass.ToJson());
        Assert.False(roundTrip.ReadOnly);
        Assert.Equal("27.0.0+2493426", roundTrip.MatchBuild);
        Assert.Equal(0x175DA000u, roundTrip.ImageSize);
        Assert.Equal(0x6AE563A1u, roundTrip.PeTimestamp);
        // Game Pass live vector: 8,213 competition objects at [module+0x56743C0]+0x40, 8/11/2026.
        Assert.Equal(0x56743C0, gamePass.Competition!.ListRootRva);
        Assert.Equal(0x40, gamePass.Competition.ListOffset);
        const ulong module = 0x10000000;
        var memory = new FakeMemory();
        // PE bytes observed from the running Game Pass module on 4/11/2026.
        var dos = memory.Map(module, 64);
        dos[0] = 0x4D; dos[1] = 0x5A; dos[0x3C] = 0x80;
        var pe = memory.Map(module + 0x80, 12);
        new byte[] { 0x50, 0x45, 0x00, 0x00, 0x64, 0x86, 0x12, 0x00, 0xA1, 0x63, 0xE5, 0x6A }.CopyTo(pe, 0);
        System.Text.Encoding.ASCII.GetBytes("27.0.0+2493426").CopyTo(memory.Map(module + 0x4AA1513, 14), 0);
        Assert.True(GameSession.ProfileMatches(memory, module, 0x175DA000, roundTrip));
        Assert.False(GameSession.ProfileMatches(memory, module, 0x175DB000, roundTrip));
        pe[8] ^= 1;
        Assert.False(GameSession.ProfileMatches(memory, module, 0x175DA000, roundTrip));
    }

    [Fact]
    public void Updated_build_embeds_the_roots_and_vtables_observed_in_game()
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var p = Assert.Single(profiles, p => p.Build == "27.0.0+2493426");

        // find-offsets and rootvecs, FM27 2493426 with the save loaded, 3/11/2026.
        Assert.Equal(0x4AE3492u, p.BuildStringRva);
        Assert.Equal(0x57057D0u, p.PlayerListRootRva + (uint)p.PlayerListBeginOffset);
        Assert.Equal(new[] { 0x4D29228u, 0x4F77A08u }, p.PlayerVtableRvas);
        Assert.Equal(0x56F7078u, p.Nation!.ListRootRva);
        Assert.Equal(0x4CE1658u, p.Nation.VtableRva);
        Assert.Equal(0x56F7070, p.Competition!.ListRootRva);
        Assert.Equal(0x4CE1AD8u, p.Competition.VtableRva);
        Assert.Equal(0x56F4A98, p.Award!.ListRootRva);
        Assert.Equal(0x4CF8078u, p.Award.VtableRva);
    }

    [Fact]
    public void Fm27_profile_is_built_in_when_no_offsets_folder_exists()
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var p = Assert.Single(profiles, p => p.Build == "27.0.0+2486695");
        Assert.Equal(0x57037C0u, p.PlayerListRootRva);
    }

    [Fact]
    public void Affiliation_capabilities_survive_profile_roundtrip_without_enabling_the_old_build()
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var updated = Assert.Single(profiles, p => p.Build == "27.0.0+2493426");
        var lists = OffsetProfile.Load(updated.ToJson()).Club!.Lists!;
        // FM27 2493426: offsets and capability recorded in re/FM27-findings.md.
        Assert.Equal(0x10, lists.AffiliationAnnualFee);
        Assert.Equal(0x24, lists.AffiliationEnd);
        Assert.True(lists.AffiliationStartEditable);
        Assert.Equal(0x33, lists.AffiliationFriendlyChance);
        Assert.Equal(0x36, lists.AffiliationParentWagePercent);
        Assert.Equal(0x1C, lists.AffiliationPlayFriendlies);
        Assert.Equal(0x1C, lists.AffiliationFirstOption);
        Assert.Equal(0x1C, lists.AffiliationPlayersOnLoan);
        Assert.Equal(0x1D, lists.AffiliationScoutingShared);

        var old = Assert.Single(profiles, p => p.Build == "27.0.0+2486695");
        var oldLists = OffsetProfile.Load(old.ToJson()).Club!.Lists!;
        Assert.Null(oldLists.AffiliationAnnualFee);
        Assert.Null(oldLists.AffiliationEnd);
        Assert.False(oldLists.AffiliationStartEditable);
        Assert.Null(oldLists.AffiliationFriendlyChance);
        Assert.Null(oldLists.AffiliationParentWagePercent);
        Assert.Null(oldLists.AffiliationPlayFriendlies);
        Assert.Null(oldLists.AffiliationFirstOption);
        Assert.Null(oldLists.AffiliationPlayersOnLoan);
        Assert.Null(oldLists.AffiliationScoutingShared);
    }

    [Fact]
    public void Profile_in_offsets_folder_replaces_the_built_in_one_for_its_build()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var json = File.ReadAllText(TestWorld.ProfilePath).Replace("\"0x57037C0\"", "\"0x1234560\"");
            File.WriteAllText(Path.Combine(dir, "override.json"), json);
            var p = Assert.Single(GameSession.LoadProfiles(dir), p => p.Build == "27.0.0+2486695");
            Assert.Equal(0x1234560u, p.PlayerListRootRva);
        }
        finally { Directory.Delete(dir, true); }
    }
}
