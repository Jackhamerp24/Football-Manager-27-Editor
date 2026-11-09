using Fm27Editor.Core.History;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The edit history. Expected values are worked by hand: what a save wrote and what it replaced. The tests stand in for
/// WindowsProcessMemory.OnWrite by recording each write the way that hook does (old bytes, new bytes).
/// </summary>
[Collection("DateSetting")]
public class ChangeJournalTests
{
    const ulong At = 0x5000_0000;

    readonly FakeMemory _m = new();
    readonly ChangeJournal _j = new();

    public ChangeJournalTests() => _m.Map(At, 0x100);

    void Write(ulong address, params byte[] value)
    {
        var before = _m.Read(address, value.Length);
        Assert.True(_m.TryWrite(address, value));
        _j.Record(address, before, value);
    }

    [Fact]
    public void A_group_keeps_what_each_write_replaced_and_merges_repeated_writes_to_one_value()
    {
        _m.TryWrite(At, [10]);
        using (_j.Begin("Player", "Jamal Baptiste", "Page save"))
        {
            Write(At, 12);
            Write(At, 14);
        }
        var group = Assert.Single(_j.Groups);
        var entry = Assert.Single(group.Entries);
        Assert.Equal((byte)10, entry.Before[0]);
        Assert.Equal((byte)14, entry.After[0]);
        Assert.Equal("10", entry.BeforeText);
        Assert.Equal("14", entry.AfterText);
    }

    [Fact]
    public void Values_read_as_what_they_are_not_as_raw_numbers()
    {
        // Worked by hand: day 60 of 2026 is 1 March (31 + 28 + 1); bytes 11 22 33 are B, G, R so the colour is #332211; 0x3FC00000 is 1.5f.
        var layout = Fm27Editor.Core.Settings.DateSetting.Layout;
        Fm27Editor.Core.Settings.DateSetting.SettingPathOverride = Path.Combine(Path.GetTempPath(), "fm27-date-journal-" + Guid.NewGuid().ToString("N") + ".txt");   // never the player's own file
        try
        {
            Fm27Editor.Core.Settings.DateSetting.Set(Fm27Editor.Core.Settings.DateLayout.YearMonthDay);
            Assert.Equal("2026-03-01", ChangeEntry.Format([60, 0, 0xEA, 0x07], new ChangeLabel("d", Kind: ChangeValueKind.Date)));
            Fm27Editor.Core.Settings.DateSetting.Set(Fm27Editor.Core.Settings.DateLayout.DayMonthYear);      // the history follows the player's date format
            Assert.Equal("01-03-2026", ChangeEntry.Format([60, 0, 0xEA, 0x07], new ChangeLabel("d", Kind: ChangeValueKind.Date)));
        }
        finally { Fm27Editor.Core.Settings.DateSetting.Set(layout); Fm27Editor.Core.Settings.DateSetting.SettingPathOverride = null; }
        Assert.Equal("#332211", ChangeEntry.Format([0x11, 0x22, 0x33], new ChangeLabel("c", Kind: ChangeValueKind.Colour)));
        Assert.Equal("1.5", ChangeEntry.Format([0x00, 0x00, 0xC0, 0x3F], new ChangeLabel("f", Kind: ChangeValueKind.Float)));
        Assert.Equal("-1", ChangeEntry.Format([0xFF], new ChangeLabel("s", Kind: ChangeValueKind.Signed)));
    }

    [Fact]
    public void A_multi_value_write_shows_only_the_parts_that_changed_and_flags_show_the_boxes()
    {
        _m.TryWrite(At, [12, 45, 14, 3, 12]);
        string[] parts = ["Rating", "Max Age", "Min Age", "", "Knowledge"];
        using (_j.Begin("Nation", "France", "Page save", (a, _) => a == At ? new ChangeLabel("Preference", Kind: ChangeValueKind.Signed, PartNames: parts) : null)) Write(At, 14, 45, 14, 3, 12);
        var preference = _j.Groups[0].Entries[0];
        Assert.Equal("Rating 12", preference.BeforeText);
        Assert.Equal("Rating 14", preference.AfterText);

        // bit 1 (value 2) is "Has Heating": 0b101 -> 0b111 switches it on and nothing else.
        _m.TryWrite(At + 0x20, [5]);
        string[] bits = ["Is Covered", "Has Heating", "", "", "", "", "", ""];
        using (_j.Begin("Stadium", "Stade", "Page save", (a, _) => a == At + 0x20 ? new ChangeLabel("Options", Kind: ChangeValueKind.Flags, PartNames: bits) : null)) Write(At + 0x20, 7);
        var flags = _j.Groups[1].Entries[0];
        Assert.Equal("Has Heating off", flags.BeforeText);
        Assert.Equal("Has Heating on", flags.AfterText);
    }

    [Fact]
    public void A_writer_can_announce_how_its_bytes_read_and_the_journal_uses_it()
    {
        _m.TryWrite(At, [1, 2, 3]);
        using (_j.Begin("Nation", "France", "Page save"))
        using (ChangeContext.Describe(new ChangeLabel("Home Shirt Background", Kind: ChangeValueKind.Colour))) Write(At, 0x11, 0x22, 0x33);
        var entry = _j.Groups[0].Entries[0];
        Assert.Equal("Home Shirt Background", entry.Name);
        Assert.Equal("#030201", entry.BeforeText);
        Assert.Equal("#332211", entry.AfterText);
    }

    [Fact]
    public void A_write_put_back_to_its_old_value_inside_one_group_leaves_nothing()
    {
        _m.TryWrite(At, [10]);
        using (_j.Begin("Player", "X", "Page save")) { Write(At, 12); Write(At, 10); }
        Assert.Empty(_j.Groups);
    }

    [Fact]
    public void Writes_outside_a_group_or_into_memory_the_editor_allocated_are_not_recorded()
    {
        Write(At, 1);
        _j.NoteAllocation(At + 0x80, 0x40);
        using (_j.Begin("Club", "Man Utd", "Page save")) { Write(At + 0x80, 5); }
        Assert.Empty(_j.Groups);
    }

    [Fact]
    public void Undo_restores_the_old_bytes_and_is_refused_when_the_game_changed_the_value_since()
    {
        _m.TryWrite(At, [3, 0]);
        using (_j.Begin("Nation", "Brazil", "Page save")) Write(At, 9, 0);
        var entry = _j.Groups[0].Entries[0];

        Assert.True(_m.TryWrite(At, [7, 0]));   // the game moved on
        Assert.False(_j.Undo(entry, _m, out var error));
        Assert.Equal("The value changed since this edit", error);
        Assert.Equal((byte)7, _m.Read(At, 1)[0]);

        Assert.True(_m.TryWrite(At, [9, 0]));
        Assert.True(_j.Undo(entry, _m, out _));
        Assert.Equal([(byte)3, (byte)0], _m.Read(At, 2));
        Assert.True(entry.Undone);
        Assert.False(_j.Undo(entry, _m, out var again));
        Assert.Equal("Already undone", again);
    }

    [Fact]
    public void Undoing_a_group_restores_every_entry_and_reports_the_ones_it_could_not()
    {
        _m.TryWrite(At, [1]); _m.TryWrite(At + 4, [2]); _m.TryWrite(At + 8, [3]);
        using (_j.Begin("Competition", "Premier League", "Page save")) { Write(At, 11); Write(At + 4, 22); Write(At + 8, 33); }
        Assert.True(_m.TryWrite(At + 4, [99]));   // changed by the game after the save

        var (restored, errors) = _j.Undo(_j.Groups[0], _m);
        Assert.Equal(2, restored);
        Assert.Single(errors);
        Assert.Equal([(byte)1, (byte)99, (byte)3], [_m.Read(At, 1)[0], _m.Read(At + 4, 1)[0], _m.Read(At + 8, 1)[0]]);
    }

    [Fact]
    public void Saves_of_the_same_thing_within_the_window_are_one_group_other_things_are_separate()
    {
        using (_j.Begin("Player", "Cooper", "Page save")) Write(At, 1);
        using (_j.Begin("Player", "Cooper", "Page save")) Write(At + 4, 2);
        using (_j.Begin("Player", "Baptiste", "Page save")) Write(At + 8, 3);
        Assert.Equal(2, _j.Groups.Count);
        Assert.Equal(2, _j.Groups[0].Entries.Count);
        Assert.Equal("Baptiste", _j.Groups[1].Entity);
    }

    [Fact]
    public void A_scope_opened_inside_another_joins_it_and_the_outer_names_the_group()
    {
        using (_j.Begin("Club", "Man Utd", "Page save"))
        {
            using (_j.Begin("Player", "Inner", "Heal")) Write(At, 1);
            Write(At + 4, 2);
        }
        var group = Assert.Single(_j.Groups);
        Assert.Equal("Man Utd", group.Entity);
        Assert.Equal(2, group.Entries.Count);
    }

    [Fact]
    public void Labels_come_from_the_labeler_and_attributes_show_in_display_units()
    {
        _m.TryWrite(At, [55]);
        using (_j.Begin("Player", "Baptiste", "Page save", (address, _) => address == At ? new ChangeLabel("First Touch", 5) : null)) Write(At, 60);
        var entry = _j.Groups[0].Entries[0];
        Assert.Equal("First Touch", entry.Name);
        Assert.Equal("11", entry.BeforeText);
        Assert.Equal("12", entry.AfterText);
    }

    [Fact]
    public void A_write_nothing_names_takes_the_name_of_the_action_that_made_it()
    {
        using (_j.Begin("Club", "Man City", "Sponsor: Total")) Write(At, 1, 0, 0, 0);
        using (_j.Begin("Club", "Man City", "Debt: Dates")) { Write(At + 8, 5); Write(At + 12, 6); }
        Assert.Equal("Sponsor: Total", _j.Groups[0].Entries[0].Name);
        Assert.Equal(["Debt: Dates (1)", "Debt: Dates (2)"], _j.Groups[1].Entries.Select(e => e.Name));
    }

    [Fact]
    public void A_write_takes_the_name_its_writer_announced_and_the_outermost_announcement_wins()
    {
        using (_j.Begin("Player", "Diomande", "Destroy"))
        {
            using (ChangeContext.Describe("Morale")) Write(At, 3);
            using (ChangeContext.Describe("Condition")) { using (ChangeContext.Describe("inner")) Write(At + 4, 5); }
            Write(At + 8, 7);
        }
        Assert.Equal(["Morale", "Condition", "Destroy"], _j.Groups[0].Entries.Select(e => e.Name));
        Assert.Null(ChangeContext.Current);
    }

    [Fact]
    public void A_write_holding_several_numbers_is_shown_as_each_of_them()
    {
        _m.TryWrite(At, [150, 0, 170, 0]);
        using (_j.Begin("Staff", "X", "Page save", (a, _) => new ChangeLabel("Current / Potential Ability", 1, 2))) Write(At, 160, 0, 180, 0);
        var entry = _j.Groups[0].Entries[0];
        Assert.Equal("150 / 170", entry.BeforeText);
        Assert.Equal("160 / 180", entry.AfterText);
    }

    [Fact]
    public void Unlabelled_changes_are_named_by_address_and_size()
    {
        using (_j.Begin("Other", "x", "Page save")) Write(At + 0x10, 1, 2, 3, 4);
        Assert.Equal($"0x{At + 0x10:X} (4 bytes)", _j.Groups[0].Entries[0].Name);
    }
}
