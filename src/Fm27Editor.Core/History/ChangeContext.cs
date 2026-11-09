using System.Runtime.CompilerServices;

namespace Fm27Editor.Core.History;

/// <summary>
/// What the code writing right now is changing ("Morale", "Wage"). A writer announces it around its write, the journal reads it
/// as the write happens, and the history shows that name instead of an address. The outermost announcement wins.
/// </summary>
public static class ChangeContext
{
    [ThreadStatic] static ChangeLabel? current;

    public static string? Current => current?.Name;
    public static ChangeLabel? CurrentLabel => current;

    public static IDisposable Describe(string name) => Describe(new ChangeLabel(name));

    /// <summary>Announces a name together with how the bytes read (colour, float, named parts).</summary>
    public static IDisposable Describe(ChangeLabel label)
    {
        if (current is not null) return Nothing.Instance;
        current = label;
        return new Scope();
    }

    /// <summary>
    /// The announced name, or else one read off the code doing the write: the nearest Fm27Editor.Core method that is not the
    /// memory layer ("NationKits.SetColour" → "Nation Kits: Colour"). Keeps every writer readable without a hand-written label.
    /// </summary>
    public static string? CallerName()
    {
        try
        {
            foreach (var frame in new System.Diagnostics.StackTrace(2, false).GetFrames())
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                while (type?.DeclaringType is not null && type.Name.StartsWith('<')) type = type.DeclaringType;
                string? ns = type?.Namespace;
                if (method is null || type is null || ns is null || !ns.StartsWith("Fm27Editor.Core", StringComparison.Ordinal)
                    || ns.EndsWith(".Memory", StringComparison.Ordinal) || ns.EndsWith(".History", StringComparison.Ordinal) || ns.Contains(".Tests", StringComparison.Ordinal)) continue;
                string name = method.Name;
                if (name.StartsWith('<')) { int end = name.IndexOf('>'); name = end > 1 ? name[1..end] : name; }
                if (name.Length > 3 && name.StartsWith("Set", StringComparison.Ordinal) && char.IsUpper(name[3])) name = name[3..];
                string owner = type.Name; int tick = owner.IndexOf('`'); if (tick > 0) owner = owner[..tick];
                owner = owner.EndsWith("Writer", StringComparison.Ordinal) && owner.Length > 6 ? owner[..^6] : owner;
                return $"{PlayerChangeLabels.Words(owner)}: {PlayerChangeLabels.Words(name)}";
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>"SetPlayingTimeHappiness" → "Playing Time Happiness".</summary>
    public static IDisposable DescribeMember([CallerMemberName] string? member = null) =>
        member is null ? Nothing.Instance : Describe(PlayerChangeLabels.Words(member.StartsWith("Set", StringComparison.Ordinal) && member.Length > 3 ? member[3..] : member));

    sealed class Scope : IDisposable { public void Dispose() => current = null; }
    sealed class Nothing : IDisposable { public static readonly Nothing Instance = new(); public void Dispose() { } }
}
