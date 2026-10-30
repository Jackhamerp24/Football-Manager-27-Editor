using Fm27Editor.Core.Game;

namespace Fm27Editor.Core.Tests;

public class ProfileLoadingTests
{
    [Fact]
    public void Fm27_profile_is_built_in_when_no_offsets_folder_exists()
    {
        var profiles = GameSession.LoadProfiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var p = Assert.Single(profiles, p => p.Build == "27.0.0+2486695");
        Assert.Equal(0x57037C0u, p.PlayerListRootRva);
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
