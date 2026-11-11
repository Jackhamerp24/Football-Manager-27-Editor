using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.MassEdit;

/// <summary>
/// One field a mass edit writes. Read/Write are the caller's guarded accessors; the planner only computes values.
/// Write receives the before value the plan read so writers can keep their stale checks.
/// </summary>
public sealed record MassFieldSpec<T>(
    ushort FieldId,
    string Label,
    Func<T, long?> Read,
    Action<T, long, long> Write,
    long Value,
    MassEditMode Mode,
    long Min,
    long Max,
    Action<long>? Validate = null);
