using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Changes observed culture fields after validating team, holder, vector membership and the full record.</summary>
public sealed class ClubVisionWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetImportance(ulong team, ClubVisionEntry entry, int importance)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no club vision offsets");
        if (!layout.VisionImportanceEditable || layout.Importance < 0 || layout.Importance >= layout.CultureSize)
            throw new InvalidOperationException("Club vision importance is unavailable for this build");
        if (importance is < byte.MinValue or > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(importance));
        if (entry.Snapshot is not { } snapshot || snapshot.Team != team)
            throw new StaleObjectException("Club vision snapshot is unavailable");
        var current = new ClubListReader(memory, profile, moduleBase).ReadClubVision(team).FirstOrDefault(r => r.Address == entry.Address);
        if (current?.Snapshot != snapshot) throw new StaleObjectException("The club vision or its containing list changed since it was read");
        ulong address = entry.Address + (ulong)layout.Importance;
        byte[] value = [(byte)importance];
        if (!memory.TryWrite(address, value) || !memory.Read(address, 1).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Could not write club vision importance at 0x{address:X}");
    }
}
