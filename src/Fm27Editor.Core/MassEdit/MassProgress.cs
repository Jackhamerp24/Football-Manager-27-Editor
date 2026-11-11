namespace Fm27Editor.Core.MassEdit;

public readonly record struct MassProgress(int Done, int Total, string Phase)
{
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);
}

public sealed record MassSkip(int TargetIndex, ushort FieldId, string Reason);

public sealed record MassResult(int Written, int Skipped, IReadOnlyList<MassSkip> Skips);
