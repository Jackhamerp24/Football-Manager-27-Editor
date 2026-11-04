using Fm27Editor.App;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App.Tests;

public class NationRivalryDraftTests
{
    [Fact]
    public void Pending_rival_values_validate_and_keep_the_original_snapshot_after_refresh()
    {
        var row = new NationRivalry(1, 2, 3, 19, 3, 4, "Italy", 60, "original", 19);
        var refreshed = row with { Value = 62, Snapshot = "changed" };
        var draft = new NationRivalryDraft();
        draft.Set(row, "61");
        Assert.Equal("61", draft.TextFor(refreshed));
        Assert.True(draft.TryParse(out var values));
        var value = Assert.Single(values);
        Assert.Equal(61, value.Value);
        Assert.Equal(row, value.Snapshot);
        draft.Set(refreshed, "101");
        Assert.False(draft.TryParse(out values));
        Assert.Empty(values);
        draft.Set(refreshed, "61");
        Assert.True(draft.TryParse(out values));
        Assert.Equal(row, Assert.Single(values).Snapshot);
        draft.Set(refreshed, "60");
        Assert.Equal(0, draft.Count);
        Assert.True(draft.TryParse(out values));
        Assert.Empty(values);
    }
}
