using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

public class BanLabelsTests
{
    /// <summary>Codes FMRTE 27 wrote when each entry was chosen on Kobbie Mainoo's ban (2/11/2026).</summary>
    [Theory]
    [InlineData(1, "Competition")]
    [InlineData(5, "Domestic Cup")]
    [InlineData(6, "Domestic League")]
    [InlineData(10, "All nation competitions")] // shown by FMRTE for Oxlade-Chamberlain's game ban
    [InlineData(15, "All continent competitions")]
    [InlineData(20, "All international matches")]
    [InlineData(25, "Global Ban")]
    [InlineData(26, "Unavailable to play club football")]
    [InlineData(28, "Suffering from undisclosed injury")]
    [InlineData(29, "Cannot play football due to medical reasons")]
    [InlineData(30, "Cannot play football due to personal reasons")]
    [InlineData(31, "Holidays")]
    [InlineData(34, "Isn't going to be registered.")]
    [InlineData(38, "Cannot play club football")]
    [InlineData(39, "Cannot play club football in official matches")]
    [InlineData(2, "Code 2")]
    public void Scope_names_match_FMRTE(int code, string name) => Assert.Equal(name, BanLabels.ScopeName(code));

    [Theory]
    [InlineData(2, "Sending off")] // FMRTE default for a new ban; Boateng's game ban
    [InlineData(4, "Misconduct")]
    [InlineData(-1, "None")]       // also Oxlade-Chamberlain's game ban
    [InlineData(6, "Code 6")]      // 92 game bans on 8/11/2026; not in FMRTE's list
    public void Reason_names_match_FMRTE(int code, string name) => Assert.Equal(name, BanLabels.ReasonName(code));
}
