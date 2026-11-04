using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Languages from FM27 RAM: player + 0x378 → vector of pointers to 16-byte records (Language*, value). Record bytes are
/// Kylian Mbappé's as dumped on 22/11/2026 (language vtable +0x4F64938, ID +0x8, name +0x18); expected values are FMRTE 27's
/// Languages tab: Spanish 8, English 5, Arabic 6.
/// </summary>
public class LanguageTests
{
    const ulong Vector = 0x6000_0000, Records = 0x6001_0000, Langs = 0x6002_0000;
    readonly TestWorld _w = new();

    public LanguageTests()
    {
        var v = _w.Memory.Map(Vector, 0x18);
        for (int i = 0; i < 3; i++) TestWorld.Set64(v, i * 8, Records + (ulong)i * 0x10);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x378, [.. BitConverter.GetBytes(Vector), .. BitConverter.GetBytes(Vector + 0x18), .. BitConverter.GetBytes(Vector + 0x18)]));
        var r = _w.Memory.Map(Records, 0x30);
        (int Id, string Name, byte Value)[] langs = [(23, "Spanish", 8), (7, "English", 5), (1, "Arabic", 6)];
        for (int i = 0; i < langs.Length; i++)
        {
            ulong lang = Langs + (ulong)i * 0x1000;
            TestWorld.Set64(r, i * 0x10, lang);
            r[i * 0x10 + 8] = langs[i].Value;
            var l = _w.Memory.Map(lang, 0x30);
            TestWorld.Set64(l, 0, TestWorld.ModuleBase + 0x4F64938);
            BitConverter.GetBytes(langs[i].Id).CopyTo(l, 8);
            TestWorld.Set64(l, 0x18, lang + 0x800);
            _w.Memory.MapString(lang + 0x800, langs[i].Name);
        }
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    IReadOnlyList<PlayerLanguage> Read() => new LanguageReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Player());

    [Fact]
    public void Reads_the_languages_FMRTE_shows_for_Mbappe()
    {
        var l = Read();
        Assert.Equal(["Spanish", "English", "Arabic"], l.Select(x => x.Name));
        Assert.Equal([23, 7, 1], l.Select(x => x.LanguageId));
        Assert.Equal([8, 5, 6], l.Select(x => x.Value));
    }

    [Fact]
    public void Value_writes_one_byte_and_refuses_stale_or_detached_records()
    {
        var writer = new LanguageWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var before = _w.Bytes(Records, 0x30);
        var l = Read();
        writer.SetValue(Player(), l[0], 9); // FMRTE showed Spanish 9 after this write
        before[0x8] = 9;
        Assert.Equal(before, _w.Bytes(Records, 0x30));
        Assert.Equal(9, Read()[0].Value);
        Assert.Throws<StaleObjectException>(() => writer.SetValue(Player(), l[0], 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetValue(Player(), Read()[1], 11));
        var english = Read()[1];
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x380, BitConverter.GetBytes(Vector + 8)));
        Assert.Throws<StaleObjectException>(() => writer.SetValue(Player(), english, 6));
        Assert.Equal(5, _w.Bytes(Records, 0x30)[0x18]);
    }

    // Add/remove follow FMRTE 27's own writes, diffed on Harry Kane (2/11/2026): adding English 4 to a full one-item vector
    // gave a new record "Language* | 04 | zeros" and a new vector buffer; removing it moved only the end pointer back 8.

    const ulong Pool = 0x7000_0000;
    ulong _next = Pool;
    readonly List<int> _allocations = [];
    ulong Allocate(int bytes) { _allocations.Add(bytes); ulong at = _next; _w.Memory.Map(at, bytes); _next += 0x1_0000; return at; }

    LanguageEntry Catalogued(string name) =>
        new LanguageReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Catalogue([Player()]).Single(l => l.Name == name);

    LanguageEntry German()
    {
        const ulong lang = Langs + 0x3000;
        var l = _w.Memory.Map(lang, 0x30);
        TestWorld.Set64(l, 0, TestWorld.ModuleBase + 0x4F64938);
        BitConverter.GetBytes(10).CopyTo(l, 8);
        TestWorld.Set64(l, 0x18, lang + 0x800);
        _w.Memory.MapString(lang + 0x800, "German");
        return new(lang, 10, "German");
    }

    [Fact]
    public void Catalogue_lists_each_language_once_by_name() =>
        Assert.Equal(["Arabic", "English", "Spanish"], new LanguageReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Catalogue([Player(), Player()]).Select(l => l.Name));

    [Fact]
    public void Add_to_a_full_vector_writes_a_new_record_and_moves_the_vector()
    {
        var oldRecords = _w.Bytes(Records, 0x30);
        var german = German();
        new LanguageWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Add(Player(), Read(), german, 4, Allocate);

        Assert.Equal([0x10, 6 * 8], _allocations);
        byte[] record = [.. BitConverter.GetBytes(german.Address), 4, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(record, _w.Bytes(Pool, 0x10));
        ulong buffer = Pool + 0x1_0000;
        byte[] header = [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + 4 * 8), .. BitConverter.GetBytes(buffer + 6 * 8)];
        Assert.Equal(header, _w.Bytes(TestWorld.Mainoo + 0x378, 24));
        byte[] pointers = [.. BitConverter.GetBytes(Records), .. BitConverter.GetBytes(Records + 0x10), .. BitConverter.GetBytes(Records + 0x20), .. BitConverter.GetBytes(Pool)];
        Assert.Equal(pointers, _w.Bytes(buffer, 32));
        Assert.Equal(oldRecords, _w.Bytes(Records, 0x30));
        Assert.Equal([8, 5, 6, 4], Read().Select(l => l.Value));
    }

    [Fact]
    public void Add_with_spare_capacity_appends_in_place()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x388, BitConverter.GetBytes(Vector + 0x20)));
        _w.Memory.Map(Vector + 0x18, 8);
        new LanguageWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Add(Player(), Read(), German(), 7, Allocate);

        Assert.Equal([0x10], _allocations);
        Assert.Equal(Pool, BitConverter.ToUInt64(_w.Bytes(Vector + 0x18, 8)));
        Assert.Equal(Vector, BitConverter.ToUInt64(_w.Bytes(TestWorld.Mainoo + 0x378, 8)));
        Assert.Equal(Vector + 0x20, BitConverter.ToUInt64(_w.Bytes(TestWorld.Mainoo + 0x380, 8)));
        Assert.Equal(["Spanish", "English", "Arabic", "German"], Read().Select(l => l.Name));
    }

    [Fact]
    public void Remove_moves_later_pointers_up_and_only_the_end_back()
    {
        var oldRecords = _w.Bytes(Records, 0x30);
        var l = Read();
        new LanguageWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Remove(Player(), l, l[0]);

        byte[] header = [.. BitConverter.GetBytes(Vector), .. BitConverter.GetBytes(Vector + 0x10), .. BitConverter.GetBytes(Vector + 0x18)];
        Assert.Equal(header, _w.Bytes(TestWorld.Mainoo + 0x378, 24));
        Assert.Equal([.. BitConverter.GetBytes(Records + 0x10), .. BitConverter.GetBytes(Records + 0x20)], _w.Bytes(Vector, 16));
        Assert.Equal(oldRecords, _w.Bytes(Records, 0x30));
        Assert.Equal(["English", "Arabic"], Read().Select(x => x.Name));
    }

    [Fact]
    public void Add_and_remove_refuse_stale_lists_and_duplicates()
    {
        var writer = new LanguageWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var l = Read();
        Assert.Throws<InvalidOperationException>(() => writer.Add(Player(), l, Catalogued("English"), 4, Allocate));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Add(Player(), l, German(), 0, Allocate));
        writer.Remove(Player(), l, l[2]);
        var header = _w.Bytes(TestWorld.Mainoo + 0x378, 24);
        Assert.Throws<StaleObjectException>(() => writer.Remove(Player(), l, l[1]));
        Assert.Throws<StaleObjectException>(() => writer.Add(Player(), l, German(), 4, Allocate));
        Assert.Equal(header, _w.Bytes(TestWorld.Mainoo + 0x378, 24));
        Assert.Empty(_allocations);
    }

    [Fact]
    public void Add_and_remove_refuse_a_list_whose_records_changed_at_the_same_count()
    {
        var writer = new LanguageWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var l = Read();
        Assert.True(_w.Memory.TryWrite(Records + 0x18, [6])); // the game raised English 5 → 6
        Assert.Throws<StaleObjectException>(() => writer.Remove(Player(), l, l[0]));
        Assert.True(_w.Memory.TryWrite(Records + 0x18, [5]));
        Assert.True(_w.Memory.TryWrite(Vector, BitConverter.GetBytes(Records + 0x20))); // a different record in slot 0
        Assert.Throws<StaleObjectException>(() => writer.Add(Player(), l, German(), 4, Allocate));
        Assert.Empty(_allocations);
        Assert.Equal(Vector + 0x18, BitConverter.ToUInt64(_w.Bytes(TestWorld.Mainoo + 0x380, 8)));
    }
}
