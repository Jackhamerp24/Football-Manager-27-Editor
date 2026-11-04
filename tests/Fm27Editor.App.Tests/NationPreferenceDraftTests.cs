using Fm27Editor.App;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App.Tests;

public class NationPreferenceDraftTests
{
    [Fact]
    public void Draft_validates_values_and_keeps_the_original_record_across_refresh()
    {
        var row = new NationPreference(1, 2, 3, 19, 3, 4, "Belgium", 12, 14, 45, 12, "original", 19);
        var draft = new NationPreferenceDraft();
        draft.Set(row, 1, "13");
        draft.Set(row with { Snapshot = "new", Knowledge = 11 }, 2, "15");
        Assert.True(draft.TryParse(out var values));
        var pending = Assert.Single(values);
        Assert.Equal((row, 12, 13, 15, 45), pending);
        draft.Set(row, 3, "10");
        Assert.False(draft.TryParse(out values));
        Assert.Empty(values);
        draft.Set(row, 3, "45");
        draft.Set(row, 2, "14");
        draft.Set(row, 1, "12");
        Assert.Equal(0, draft.Count);
    }
}
