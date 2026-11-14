using Fm27Editor.App;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App.Tests;

[Collection("Language settings")]
public sealed class VietnameseFootballTermsTests
{
    [Fact]
    public void Player_attributes_and_preferred_moves_have_vietnamese_labels()
    {
        var file = Path.Combine(Path.GetTempPath(), "fm27-vi-" + Guid.NewGuid().ToString("N"), "language.txt");
        var oldPath = AppLanguage.SettingPathOverride;
        var oldLanguage = AppLanguage.Current;
        AppLanguage.SettingPathOverride = file;
        try
        {
            AppLanguage.Save("vi");
            AppLanguage.Load();
            var missingAttributes = PlayerFields.All.Where(f => Enum.TryParse<PlayerAttribute>(f.Id, out _))
                .Select(f => f.Label).Where(label => AppLanguage.PlayerAttribute(label) == label).ToArray();
            var missingMoves = PreferredMovesLabels.Names.Where(label => AppLanguage.T(label) == label).ToArray();
            Assert.Empty(missingAttributes);
            Assert.Empty(missingMoves);
            var missingMotives = UnhappinessMotives.All.Values.Select(m => m.Name).Distinct().Where(label => AppLanguage.T(label) == label).ToArray();
            Assert.Empty(missingMotives);
            foreach (var label in UnhappinessMotives.All.Values.Select(m => m.Name).Distinct())
                foreach (var token in new[] { "Person 2", "Person", "Team", "Competition" })
                    Assert.Equal(System.Text.RegularExpressions.Regex.Matches(label, token).Count, System.Text.RegularExpressions.Regex.Matches(AppLanguage.T(label), token).Count);
        }
        finally
        {
            AppLanguage.Save(oldLanguage);
            AppLanguage.Load();
            AppLanguage.SettingPathOverride = oldPath;
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }
    }
}
