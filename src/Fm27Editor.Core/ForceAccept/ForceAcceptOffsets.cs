namespace Fm27Editor.Core.ForceAccept;

/// <summary>
/// Profile <c>forceAccept</c>: the RVAs of the four code sites the three force-accept presets patch. Every field is
/// optional and stays null until the site has been measured on that build, so a profile without this section simply
/// has no force-accept support and the planner reports the presets as unavailable instead of guessing an address.
/// </summary>
/// <remarks>
/// The byte sequences these RVAs point at are build-dependent too, but they are pinned in
/// <see cref="ForceAcceptPlanner"/> (from the Cheat Engine table's preset scripts and FM27Studio's patch constants,
/// both cross-checked against the live FM27 image) and a site whose bytes do not match is reported as a mismatch
/// rather than written.
/// </remarks>
public sealed class ForceAcceptOffsets
{
    /// <summary>The 6-byte conditional jump whose NOP lets every general board request through.</summary>
    public uint? BoardRequestsRva { get; init; }

    /// <summary>The 2-byte conditional jump whose NOP lets every club-vision change through.</summary>
    public uint? ClubVisionRva { get; init; }

    /// <summary>
    /// The 16-byte window whose original form stores the contract-offer status at <c>[obj+0x1ED0]</c>. The
    /// Cheat Engine reference for this preset is a guarded hook (human manager's offers only) and is not
    /// implemented, so this RVA is carried for that future work and the preset itself is never applied.
    /// </summary>
    public uint? ContractOffersRva { get; init; }

    /// <summary>The 8-byte <c>movzx r13d, byte [obj+0x800]</c> whose result decides whether an offer is accepted.</summary>
    public uint? TransferOffersRva { get; init; }
}
