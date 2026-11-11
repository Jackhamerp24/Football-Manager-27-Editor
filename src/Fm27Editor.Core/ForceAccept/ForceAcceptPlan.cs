namespace Fm27Editor.Core.ForceAccept;

/// <summary>The three presets that force an NPC side to accept the human player's offer.</summary>
/// <remarks>
/// All three are code patches rather than data flags: the Cheat Engine table's preset scripts and FM27Studio both
/// NOP or overwrite instructions in <c>game_plugin</c>. Which instructions, where, is per build — the profile's
/// <c>forceAccept</c> section holds the RVAs, <see cref="ForceAcceptPlanner"/> holds the byte sequences. The
/// contract preset is the exception: its Cheat Engine reference is a guarded hook and it is not implemented, so it
/// reports <see cref="ForceAcceptSiteState.NotImplemented"/> and stays unavailable.
/// </remarks>
public enum ForceAcceptPreset
{
    /// <summary>The board accepts every board request, including club-vision changes.</summary>
    BoardRequests,

    /// <summary>Players and staff accept contract offers as offered.</summary>
    ContractOffers,

    /// <summary>Other clubs accept transfer offers.</summary>
    TransferOffers,
}

/// <summary>What the planner found at one patch site.</summary>
public enum ForceAcceptSiteState
{
    /// <summary>The profile has no RVA for this site on this build: nothing can be planned here (fail closed).</summary>
    MissingOffset,

    /// <summary>
    /// The preset is not implemented in a form safe to apply — the Cheat Engine reference is a guarded hook, not an
    /// overwrite — so the site is never read and the preset can never be enabled until that hook is measured.
    /// </summary>
    NotImplemented,

    /// <summary>The site's memory could not be read.</summary>
    Unreadable,

    /// <summary>The site still holds the original instructions: writing the replacement would enable the preset.</summary>
    Ready,

    /// <summary>The site already holds the replacement bytes: the preset is already enabled here.</summary>
    AlreadyApplied,

    /// <summary>The site holds neither the original nor the replacement bytes — another build or another tool.</summary>
    Mismatch,
}

/// <summary>The overall result of planning one preset.</summary>
public enum ForceAcceptPlanState
{
    /// <summary>
    /// At least one site has no measured RVA, or the preset is not implemented: the preset is not supported at all.
    /// </summary>
    Unavailable,

    /// <summary>Every site has an RVA, but at least one is unreadable or holds foreign bytes: nothing may be written.</summary>
    Blocked,

    /// <summary>At least one site is ready and none is blocked: those sites could be patched.</summary>
    Appliable,

    /// <summary>Every site already holds the replacement bytes.</summary>
    Active,
}

/// <summary>The planned state of one site: where it is, what is there now, and what a write would put there.</summary>
public sealed class ForceAcceptSitePlan
{
    /// <summary>Stable identifier of the site within its preset.</summary>
    public required string Key { get; init; }

    /// <summary>RVA from the profile, or null when this build was not measured.</summary>
    public required uint? Rva { get; init; }

    /// <summary>Module base plus RVA, or null when there is no RVA.</summary>
    public required ulong? Address { get; init; }

    /// <summary>The bytes that must already be in place for a write to be allowed — the guard.</summary>
    public required byte[] Original { get; init; }

    /// <summary>The bytes a write would store in place of <see cref="Original"/>.</summary>
    public required byte[] Replacement { get; init; }

    /// <summary>The bytes read at the site, or null when the site is missing or unreadable.</summary>
    public byte[]? Current { get; init; }

    public required ForceAcceptSiteState State { get; init; }
}

/// <summary>A preview of one preset: its sites, their states, and the writes a future apply step would perform.</summary>
public sealed class ForceAcceptPlan
{
    public required ForceAcceptPreset Preset { get; init; }
    public required ForceAcceptPlanState State { get; init; }
    public required IReadOnlyList<ForceAcceptSitePlan> Sites { get; init; }

    /// <summary>
    /// The sites that hold their original bytes and may be written — empty unless the whole preset is appliable, so
    /// a blocked or unavailable preset never hands out a partial write list.
    /// </summary>
    public IReadOnlyList<ForceAcceptSitePlan> Pending =>
        State == ForceAcceptPlanState.Appliable ? Sites.Where(s => s.State == ForceAcceptSiteState.Ready).ToArray() : [];
}
