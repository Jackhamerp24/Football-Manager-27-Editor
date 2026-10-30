using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

// Each write uses the original snapshot, so a game-side change to a pending field
// remains guarded even after another field has been saved and reloaded.
public sealed class PlayerDraft(PlayerRecord original)
{
    readonly Dictionary<FieldDescriptor<PlayerRecord>, int> changes = [];
    public PlayerRecord Original { get; } = original;
    public int Count => changes.Count;
    public int Value(FieldDescriptor<PlayerRecord> field) => changes.TryGetValue(field, out var value) ? value : field.Read(Original);
    public void Stage(FieldDescriptor<PlayerRecord> field, int value)
    {
        if (field.Write is null || value < field.Min || value > field.Max) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == field.Read(Original)) changes.Remove(field); else changes[field] = value;
    }
    public PlayerRecord Save(IEditService edits)
    {
        var latest = Original;
        foreach (var (field, value) in changes.ToArray())
        {
            latest = edits.Apply(Original, field, value);
            changes.Remove(field);
        }
        return latest;
    }
    public PlayerDraft Rebase(PlayerRecord fresh)
    {
        var rebased = new PlayerDraft(fresh);
        foreach (var (field, value) in changes) rebased.Stage(field, value);
        return rebased;
    }
}
