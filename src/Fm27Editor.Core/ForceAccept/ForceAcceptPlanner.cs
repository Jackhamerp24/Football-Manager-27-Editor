using Fm27Editor.Core.History;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.ForceAccept;

/// <summary>
/// Plans, and optionally applies, the force-accept presets. <see cref="Plan"/> is read-only: for every code site a
/// preset would patch it reports where the site is, which bytes are there now, which bytes a write would store, and
/// whether the guards hold. <see cref="Apply"/> writes a plan that is appliable, re-checking the same guards
/// immediately before the first write, all as one undo group.
/// </summary>
/// <remarks>
/// Byte sequences come from the Cheat Engine table's force-accept preset scripts and from FM27Studio's patch
/// constants (the two agree, and both sequences were found in the live FM27 image); RVAs come only from the
/// profile's optional <c>forceAccept</c> section. A site whose current bytes match neither the original nor the
/// replacement is reported as a mismatch and the whole preset is blocked, so an unmeasured or foreign build is
/// never written to by mistake.
/// <para>
/// The presets patch the game's code pages, which are execute/read-only: <see cref="Apply"/> raises the page
/// protection for each write, puts it back and flushes the instruction cache (see <see cref="CodePage"/>). A
/// multi-byte patch can be torn if a thread executes it mid-write, so Apply is only for a paused game — the caller
/// says so and a running game is refused.
/// </para>
/// <para>
/// The contract preset is not implemented: its Cheat Engine reference is a hook that forces the value only for the
/// human manager's offers, which an overwrite cannot express (see the site table below). It reports
/// <see cref="ForceAcceptSiteState.NotImplemented"/> and stays unavailable until that hook is measured.
/// </para>
/// </remarks>
public sealed class ForceAcceptPlanner(OffsetProfile profile, IProcessMemory memory, ulong moduleBase, ChangeJournal? journal = null)
{
    /// <summary>One patch site: which profile field carries its RVA, which bytes replace which, and whether the preset is an implemented overwrite.</summary>
    sealed record Site(string Key, Func<ForceAcceptOffsets?, uint?> RvaFrom, byte[] Original, byte[] Replacement, bool RequiresGuardedHook = false);

    static readonly IReadOnlyDictionary<ForceAcceptPreset, Site[]> Sites = new Dictionary<ForceAcceptPreset, Site[]>
    {
        // Board: NOP the two conditional jumps that reject a request — the 6-byte jne guarding general requests and
        // the 2-byte js guarding club vision. Cheat Engine (FM26): `aob1 = 83 00 00 00 01 48 89 ?? ?? 03 00 00`
        // with `nop 6` at aob1+0x1A, and `aob2 = 0F B6 4E 6B 01 C1` with `nop 2` at aob2+0x0D. On the live FM27
        // image aob2 matches once, at RVA 0x1D52F09, where +0x0D holds `78 78` (js, a different displacement from
        // FM26's `78 7C`); aob1's FM27 equivalent is not settled (the 6-byte jne sequence has 8+ hits), so both
        // RVAs stay in the profile until measured per build.
        [ForceAcceptPreset.BoardRequests] =
        [
            new("generalRequests", o => o?.BoardRequestsRva,
                [0x0F, 0x85, 0xFD, 0x00, 0x00, 0x00], [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]),
            new("clubVision", o => o?.ClubVisionRva,
                [0x78, 0x7C], [0x90, 0x90]),
        ],
        // Contract: NOT IMPLEMENTED — the site is reported NotImplemented and never read, so no byte pattern can
        // enable the preset. The Cheat Engine preset (`K9__players-and-staff-always-accept-your-contract-offers`,
        // FM26) is a hook, not an overwrite: it replaces the 9 bytes at `aobscanmodule(glpr_paon,
        // 89 C6 41 89 86 D0 1E 00 00)` — `mov esi,eax` + `mov [r14+0x1ED0],eax` — with `jmp newmem` + `nop 4`,
        // and the trampoline forces `eax = 0x22F87` only when `[r14+8] + humanManagerOffset` (FM26: 1104) holds the
        // human manager vtable (`vtbHumanNonPlayer`, looked up by RTTI name at runtime); the store is kept, so
        // AI-to-AI offers keep their own value. The overwrite below cannot express that guard, and the FM27 window
        // (found live at RVA 0x24B8789) is a different shape — `mov r15d,eax; mov rsi,[rbp+0x1078];
        // mov [rsi+0x1ED0],eax`, object in rsi rather than r14 — so the hook's registers, the human manager vtable
        // and humanManagerOffset must all be measured per build before this preset can exist. Until then it stays
        // unavailable rather than forcing every offer in the game.
        [ForceAcceptPreset.ContractOffers] =
        [
            new("contractStatus", o => o?.ContractOffersRva,
                [0x41, 0x89, 0xC7, 0x48, 0x8B, 0xB5, 0x78, 0x10, 0x00, 0x00, 0x89, 0x86, 0xD0, 0x1E, 0x00, 0x00],
                [0x48, 0x8B, 0xB5, 0x78, 0x10, 0x00, 0x00, 0xB8, 0x87, 0x2F, 0x02, 0x00, 0x41, 0x89, 0xC7, 0x90],
                RequiresGuardedHook: true),
        ],
        // Transfer: `movzx r13d, byte [obj+0x800]` loads the answer the acceptance branch switches on; the
        // replacement sets r13d to 3 — the value both the Cheat Engine preset and FM27Studio force — and pads the
        // two leftover bytes with NOPs so the instruction length stays the same.
        [ForceAcceptPreset.TransferOffers] =
        [
            new("transferStatus", o => o?.TransferOffersRva,
                [0x44, 0x0F, 0xB6, 0xAF, 0x00, 0x08, 0x00, 0x00],
                [0x41, 0xBD, 0x03, 0x00, 0x00, 0x00, 0x90, 0x90]),
        ],
    };

    /// <summary>Plans one preset against the current memory; reads only, writes nothing.</summary>
    public ForceAcceptPlan Plan(ForceAcceptPreset preset)
    {
        var planned = new List<ForceAcceptSitePlan>(Sites[preset].Length);
        foreach (var site in Sites[preset])
        {
            // A preset whose reference is a guarded hook is never read: no byte pattern at the site can enable it,
            // and reading would only suggest it could be planned.
            if (site.RequiresGuardedHook)
            {
                uint? hookRva = site.RvaFrom(profile.ForceAccept);
                planned.Add(new ForceAcceptSitePlan
                {
                    Key = site.Key,
                    Rva = hookRva,
                    Address = hookRva is null ? null : moduleBase + hookRva.Value,
                    Original = site.Original,
                    Replacement = site.Replacement,
                    State = ForceAcceptSiteState.NotImplemented,
                });
                continue;
            }

            uint? rva = site.RvaFrom(profile.ForceAccept);
            if (rva is null)
            {
                planned.Add(new ForceAcceptSitePlan
                {
                    Key = site.Key,
                    Rva = null,
                    Address = null,
                    Original = site.Original,
                    Replacement = site.Replacement,
                    State = ForceAcceptSiteState.MissingOffset,
                });
                continue;
            }

            ulong address = moduleBase + rva.Value;
            var current = new byte[site.Original.Length];
            if (!memory.TryRead(address, current))
            {
                planned.Add(new ForceAcceptSitePlan
                {
                    Key = site.Key,
                    Rva = rva,
                    Address = address,
                    Original = site.Original,
                    Replacement = site.Replacement,
                    State = ForceAcceptSiteState.Unreadable,
                });
                continue;
            }

            var state =
                current.AsSpan().SequenceEqual(site.Replacement) ? ForceAcceptSiteState.AlreadyApplied :
                current.AsSpan().SequenceEqual(site.Original) ? ForceAcceptSiteState.Ready :
                ForceAcceptSiteState.Mismatch;
            planned.Add(new ForceAcceptSitePlan
            {
                Key = site.Key,
                Rva = rva,
                Address = address,
                Original = site.Original,
                Replacement = site.Replacement,
                Current = current,
                State = state,
            });
        }
        return new ForceAcceptPlan { Preset = preset, State = OverallState(planned), Sites = planned };
    }

    const string JournalKind = "Force Accept", JournalTitle = "Force accept";

    /// <summary>
    /// Writes the plan's pending sites: every site is re-read and must still hold its original bytes before the
    /// first write (one changed site stops the whole preset with nothing written), each replacement is written to
    /// the code page and read back, and a failure part-way puts the sites already written back. The run is one
    /// history group, so one undo restores the original bytes of every site. Returns how many sites were patched.
    /// </summary>
    /// <param name="gamePaused">
    /// True only while the game is paused: a multi-byte patch can be torn if a thread executes it mid-write. The
    /// session has no paused signal yet — the main session agrees one and the UI refuses when it is not paused.
    /// </param>
    /// <exception cref="InvalidOperationException">The game is running, or the plan is not <see cref="ForceAcceptPlanState.Appliable"/> (missing RVA, not implemented, blocked or already active).</exception>
    /// <exception cref="StaleObjectException">A site no longer holds the bytes the plan was made from; nothing was written.</exception>
    /// <exception cref="MemoryAccessException">A site could not be read, written or read back; everything this call wrote was put back.</exception>
    public int Apply(ForceAcceptPlan plan, bool gamePaused)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!gamePaused)
            throw new InvalidOperationException("The force-accept presets are applied only while the game is paused");
        if (plan.State != ForceAcceptPlanState.Appliable)
            throw new InvalidOperationException($"The {plan.Preset} preset is {plan.State}: nothing was written");
        var pending = plan.Pending;

        var written = new List<ForceAcceptSitePlan>(pending.Count);
        // Re-check every site before the first write: one stale site must block the whole preset.
        foreach (var site in pending)
        {
            var current = new byte[site.Original.Length];
            if (site.Address is not { } address || !memory.TryRead(address, current))
                throw new MemoryAccessException($"Cannot re-read {site.Key}: nothing was written");
            if (!current.AsSpan().SequenceEqual(site.Original))
                throw new StaleObjectException($"{site.Key} no longer holds its original bytes: nothing was written");
        }

        IDisposable? scope = journal?.Begin(JournalKind, Entity(plan.Preset), JournalTitle, Labels(pending));
        try
        {
            foreach (var site in pending)
            {
                try
                {
                    CodePage.Write(memory, site.Address!.Value, site.Replacement);
                    written.Add(site);
                    var readBack = new byte[site.Replacement.Length];
                    if (!memory.TryRead(site.Address.Value, readBack) || !readBack.AsSpan().SequenceEqual(site.Replacement))
                        throw new MemoryAccessException($"Read-back of {site.Key} failed");
                }
                catch
                {
                    // A half-applied preset is worse than none: put back everything this call wrote and stop.
                    foreach (var done in written) CodePage.TryWrite(memory, done.Address!.Value, done.Original);
                    throw;
                }
            }
        }
        finally { scope?.Dispose(); }
        return pending.Count;
    }

    /// <summary>
    /// Puts the original bytes back for a plan whose sites are all <see cref="ForceAcceptSiteState.AlreadyApplied"/>.
    /// Every site is re-read and must still hold the replacement before the first write; a failure part-way puts back
    /// what this call restored. The run is one history group, so one undo re-applies the preset if wanted.
    /// </summary>
    /// <param name="gamePaused">True only while the game is paused, for the same torn-instruction reason as <see cref="Apply"/>.</param>
    /// <exception cref="InvalidOperationException">The game is running, or the plan is not <see cref="ForceAcceptPlanState.Active"/>.</exception>
    /// <exception cref="StaleObjectException">A site no longer holds the replacement bytes; nothing was written.</exception>
    /// <exception cref="MemoryAccessException">A site could not be read, written or read back; everything this call wrote was put back.</exception>
    public int Restore(ForceAcceptPlan plan, bool gamePaused)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!gamePaused)
            throw new InvalidOperationException("The force-accept presets are restored only while the game is paused");
        if (plan.State != ForceAcceptPlanState.Active)
            throw new InvalidOperationException($"The {plan.Preset} preset is {plan.State}: nothing was restored");
        var sites = plan.Sites.Where(s => s.State == ForceAcceptSiteState.AlreadyApplied && s.Address is not null).ToList();

        foreach (var site in sites)
        {
            var current = new byte[site.Replacement.Length];
            if (!memory.TryRead(site.Address!.Value, current) || !current.AsSpan().SequenceEqual(site.Replacement))
                throw new StaleObjectException($"{site.Key} no longer holds the replacement bytes: nothing was restored");
        }

        var restored = new List<ForceAcceptSitePlan>(sites.Count);
        IDisposable? scope = journal?.Begin(JournalKind, Entity(plan.Preset), "Restore", Labels(sites));
        try
        {
            foreach (var site in sites)
            {
                try
                {
                    CodePage.Write(memory, site.Address!.Value, site.Original);
                    restored.Add(site);
                    var readBack = new byte[site.Original.Length];
                    if (!memory.TryRead(site.Address.Value, readBack) || !readBack.AsSpan().SequenceEqual(site.Original))
                        throw new MemoryAccessException($"Read-back of {site.Key} failed");
                }
                catch
                {
                    foreach (var done in restored) CodePage.TryWrite(memory, done.Address!.Value, done.Replacement);
                    throw;
                }
            }
        }
        finally { scope?.Dispose(); }
        return sites.Count;
    }

    /// <summary>The history group's entity: the preset's name as the history window shows it.</summary>
    static string Entity(ForceAcceptPreset preset) => preset switch
    {
        ForceAcceptPreset.BoardRequests => "Board requests",
        ForceAcceptPreset.ContractOffers => "Contract offers",
        ForceAcceptPreset.TransferOffers => "Transfer offers",
        _ => preset.ToString(),
    };

    static readonly IReadOnlyDictionary<string, string> SiteNames = new Dictionary<string, string>
    {
        ["generalRequests"] = "General requests",
        ["clubVision"] = "Club vision",
        ["contractStatus"] = "Contract status",
        ["transferStatus"] = "Transfer status",
    };

    /// <summary>Names each written site in the history, from the site keys rather than an address.</summary>
    Func<ulong, int, ChangeLabel?>? Labels(IReadOnlyList<ForceAcceptSitePlan> pending)
    {
        if (journal is null) return null;
        var byAddress = new Dictionary<ulong, ChangeLabel>();
        foreach (var site in pending.Where(s => s.Address is not null))
            byAddress[site.Address!.Value] = new ChangeLabel(SiteNames.GetValueOrDefault(site.Key, site.Key));
        return (address, _) => byAddress.GetValueOrDefault(address);
    }

    /// <summary>
    /// A missing RVA and an unimplemented preset dominate (the build was not measured, or the preset has no safe
    /// form), then anything that makes a write unsafe; only then does the preset count as active or appliable.
    /// </summary>
    static ForceAcceptPlanState OverallState(IReadOnlyList<ForceAcceptSitePlan> sites) =>
        sites.Any(s => s.State is ForceAcceptSiteState.MissingOffset or ForceAcceptSiteState.NotImplemented) ? ForceAcceptPlanState.Unavailable :
        sites.Any(s => s.State is ForceAcceptSiteState.Unreadable or ForceAcceptSiteState.Mismatch) ? ForceAcceptPlanState.Blocked :
        sites.All(s => s.State == ForceAcceptSiteState.AlreadyApplied) ? ForceAcceptPlanState.Active :
        ForceAcceptPlanState.Appliable;
}
