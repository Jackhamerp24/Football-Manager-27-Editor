using System.Security.Cryptography;
using System.Text;

namespace Fm27Editor.Core.Players;

/// <summary>The only scan scope that can call a player new to the database: a sweep of the whole database.</summary>
public static class RegenScope
{
    public const string Global = "global";
}

/// <summary>Why a scan was taken. Recorded as provenance; it never decides a classification by itself.</summary>
public static class RegenPhase
{
    public const string BeforeIntake = "before-intake";
    public const string AfterIntake = "after-intake";
    public const string Unknown = "unknown";
}

/// <summary>The ledger feature key: one row per player, per feature, per save branch.</summary>
public static class RegenFeature
{
    public const string Default = "regen-intake";
}

/// <summary>
/// One player a scan saw. Ids are text because FM27's ids have not been shown to fit in 32 bits: a number could
/// truncate two different players into one, so an id is never parsed into an integer here. Memory addresses are
/// not stored at all — they change on every load and say nothing about who a player is.
/// </summary>
public sealed record RegenObservation(
    string PersonUid,
    string? PlayerUid = null,
    DateOnly? DateOfBirth = null,
    string? ClubUid = null,
    string? TeamUid = null,
    bool? IsPlayer = null,
    bool? IsYouth = null,
    string? TransferEvidence = null,
    string? Provenance = null);

/// <summary>
/// One scan of the database. <see cref="ScanFingerprint"/> is derived from the contents rather than stored, so a
/// reloaded scan of the same state carries the same fingerprint and a pointer change shows up nowhere.
/// </summary>
public sealed record RegenSnapshot(
    string SaveId,
    string BranchId,
    DateOnly GameDate,
    bool Complete,
    IReadOnlyList<RegenObservation> Players,
    string Scope = RegenScope.Global,
    string Phase = RegenPhase.Unknown)
{
    public string ScanFingerprint => RegenFingerprint.Of(this);
}

/// <summary>
/// Content hash of a scan: save, branch, date, flags and every player's ids and facts, taken in id order so the
/// order the scanner happened to walk the database changes nothing. Two scans of the same state match; a rewind,
/// a reload of another save or a player that arrived do not.
/// </summary>
public static class RegenFingerprint
{
    public static string Of(RegenSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var canonical = new StringBuilder();
        canonical.Append(snapshot.SaveId).Append('|').Append(snapshot.BranchId).Append('|')
            .Append(snapshot.GameDate.ToString("O")).Append('|').Append(snapshot.Scope).Append('|')
            .Append(snapshot.Phase).Append('|').Append(snapshot.Complete ? '1' : '0').Append('|');
        foreach (var p in (snapshot.Players ?? []).OrderBy(p => p.PersonUid, StringComparer.Ordinal))
            canonical.Append(p.PersonUid).Append(';').Append(p.PlayerUid ?? "").Append(';')
                .Append(p.DateOfBirth?.ToString("O") ?? "").Append(';')
                .Append(p.ClubUid ?? "").Append(';').Append(p.TeamUid ?? "").Append(';')
                .Append(Mark(p.IsPlayer)).Append(';').Append(Mark(p.IsYouth)).Append(';')
                .Append(p.TransferEvidence ?? "").Append(';').Append(p.Provenance ?? "").Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    static char Mark(bool? value) => value switch { true => '1', false => '0', _ => '?' };
}

/// <summary>
/// What one player turned out to be. <see cref="ConfirmedRegen"/> is reserved for a native FM27 field or event that
/// a local check confirmed, and nothing in this feature can produce it: a high id, a young age and a missing
/// transfer history are not evidence of that kind.
/// </summary>
public enum RegenStatus
{
    /// <summary>The id was not in the baseline scan: new to the database, and nothing more than that.</summary>
    NewInDatabase,
    /// <summary>Already in the baseline and now at another club: a transfer, a loan or a promotion.</summary>
    NewToClub,
    /// <summary>New to the database and matched to an intake this feature could confirm.</summary>
    LikelyIntake,
    /// <summary>Data the decision needs is missing or unverified. Nothing may act on it.</summary>
    PendingReview,
    /// <summary>Reserved for a native, locally verified FM27 intake record. Never produced here.</summary>
    ConfirmedRegen,
}

/// <summary>One row of the preview: what a player is, and why in plain words.</summary>
public sealed record RegenCandidate(string PersonUid, RegenStatus Status, string Detail, RegenObservation Observation)
{
    /// <summary>
    /// The only statuses a later apply step may act on without asking the user. This feature itself never writes
    /// anything to the game or to a save; it only reports.
    /// </summary>
    public bool AutoEligible => Status is RegenStatus.LikelyIntake or RegenStatus.ConfirmedRegen;
}

/// <summary>How a scan was judged: classified, refused, or missing the baseline it would need.</summary>
public enum RegenScanVerdict { Classified, Quarantined, UnknownBaseline }

/// <summary>
/// The result of one diff. A quarantine carries no candidates at all — nothing is guessed at when the scans that
/// arrived cannot be trusted.
/// </summary>
public sealed record RegenDiffResult(RegenScanVerdict Verdict, string Detail, IReadOnlyList<RegenCandidate> Candidates)
{
    public bool Quarantined => Verdict == RegenScanVerdict.Quarantined;

    /// <summary>The rows a later apply step would be allowed to touch without asking.</summary>
    public IReadOnlyList<RegenCandidate> AutoEligible => Candidates.Where(c => c.AutoEligible).ToArray();
}

/// <summary>
/// An observed youth intake. Only a verified one — checked against the game's own calendar, history or screen —
/// can carry a player to <see cref="RegenStatus.LikelyIntake"/>; an unverified observation is a weak signal and
/// holds the player for review instead.
/// </summary>
public sealed record RegenIntakeEvent(string EventId, string ClubUid, DateOnly ObservedDate, string Source, bool Verified)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EventId)) throw new ArgumentException("An intake event needs an id", nameof(EventId));
        if (string.IsNullOrWhiteSpace(ClubUid)) throw new ArgumentException("An intake event needs a club", nameof(ClubUid));
    }
}

/// <summary>
/// How a scan is judged. Every threshold lives here, so nothing about a particular game build or a particular save
/// is baked into the code: the caller sets the club, the ages and the window it has actually verified.
/// </summary>
public sealed record RegenIntakeOptions(
    string? ClubUid = null,
    int? MinAge = null,
    int? MaxAge = null,
    int IntakeWindowDays = 0,
    string Feature = RegenFeature.Default)
{
    public void Validate()
    {
        if (MinAge is { } min && MaxAge is { } max && min > max)
            throw new ArgumentException($"Age range {min}–{max} has its minimum above its maximum");
        if (IntakeWindowDays < 0)
            throw new ArgumentOutOfRangeException(nameof(IntakeWindowDays), IntakeWindowDays, "The intake window cannot be negative");
        if (string.IsNullOrWhiteSpace(Feature))
            throw new ArgumentException("A ledger feature name is required", nameof(Feature));
    }

    /// <summary>Age on a given day, counting the birthday that has not come round yet as one year less.</summary>
    public static int AgeAt(DateOnly born, DateOnly on)
    {
        int age = on.Year - born.Year;
        if (born.AddYears(age) > on) age--;
        return age;
    }
}
