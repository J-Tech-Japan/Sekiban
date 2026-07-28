using ResultBoxes;
namespace Sekiban.Dcb.Storage.Checkpoints;

/// <summary>
///     SEK-G20 post-commit ambiguity resolution for a checkpoint CAS. When a conditional write DISPATCHES but its
///     response is lost (a transport failure or cancellation after the store may already have committed), the caller
///     cannot tell whether the write took. This resolver performs a BOUNDED, caller-independent re-read to decide:
///     <list type="bullet">
///         <item>a re-read within budget that shows the row reflects OUR intended committed write (by exact token AND
///         payload identity) → <see cref="CheckpointCasStatus.Committed" /> — the response was lost AFTER a durable
///         commit;</item>
///         <item>the budget is exhausted without confirmation → <see cref="CheckpointCasStatus.InDoubt" /> — typed
///         retryable, carrying the secret-safe cause. A retry uses the SAME exact expected token, so it is idempotent: if
///         the first write DID commit the token has moved and the retry cleanly rejects; if it did not, the retry
///         succeeds.</item>
///     </list>
///     It NEVER returns a false <see cref="CheckpointCasStatus.Committed" /> — a concurrent writer that won the same token
///     with a DIFFERENT payload fails the identity check, so it is reported InDoubt (we did not commit), not Committed.
/// </summary>
public static class CheckpointInDoubtResolver
{
    /// <summary>The stable default bounded verification budget. A test seam may override it per call (never in production).</summary>
    public static readonly TimeSpan DefaultVerificationBudget = TimeSpan.FromSeconds(10);

    /// <summary>The default number of bounded re-read attempts. Secondary to the time budget below.</summary>
    public const int DefaultVerificationAttempts = 3;

    public static async Task<CheckpointCasOutcome> ResolveAsync(
        Func<CancellationToken, Task<ResultBox<CheckpointSlot>>> reread,
        Func<CheckpointSlot, bool> committedByUs,
        int maxAttempts,
        Exception cause,
        TimeSpan? verificationBudget = null)
    {
        ArgumentNullException.ThrowIfNull(reread);
        ArgumentNullException.ThrowIfNull(committedByUs);

        // The caller invokes this ONLY when the write already crossed a commit-capable boundary (a dispatch/transport
        // failure), so its commit is genuinely unknown. Verification runs on a caller-INDEPENDENT, TIME-BOUNDED budget: a
        // fresh CancellationTokenSource (NEVER the caller's possibly-already-cancelled token, NEVER CancellationToken.None),
        // whose token is passed into EVERY re-read + committed-state verification. A hung/unreachable authority is
        // cancelled PROMPTLY at the budget — the provider read task exits cooperatively (it observes the token) and there is
        // ZERO background read/write/mutation after this returns. A re-read that confirms our exact write -> Committed. ANY
        // other outcome remains typed retryable InDoubt, preserving the original safe cause/token; it is NEVER downgraded to
        // ProviderFailure here (a deterministic pre-commit / schema failure is classified ProviderFailure by the PROVIDER
        // before it calls this). The closed InDoubt reason records WHY the winner is unknown:
        //   - at least one re-read SUCCEEDED but none confirmed our write => AmbiguousAfterWrite;
        //   - EVERY bounded re-read failed / timed out (authority unreachable) => VerificationUnavailable.
        using var budgetCts = new CancellationTokenSource(verificationBudget ?? DefaultVerificationBudget);
        var anyReadSucceeded = false;
        for (var attempt = 0; attempt < Math.Max(1, maxAttempts) && !budgetCts.IsCancellationRequested; attempt++)
        {
            ResultBox<CheckpointSlot> read;
            try
            {
                read = await reread(budgetCts.Token).ConfigureAwait(false);
            }
            catch
            {
                if (budgetCts.IsCancellationRequested)
                {
                    break; // the budget is exhausted (the read observed the token and cancelled) -> stop promptly
                }
                continue; // a transient unreadable authority does not prove non-commit; keep within budget
            }
            if (!read.IsSuccess)
            {
                if (budgetCts.IsCancellationRequested)
                {
                    break;
                }
                continue; // a failed ResultBox is also an unavailable read
            }
            anyReadSucceeded = true;
            if (committedByUs(read.GetValue()))
            {
                return CheckpointCasOutcome.Committed(read.GetValue());
            }
        }
        return CheckpointCasOutcome.Doubt(
            anyReadSucceeded ? CheckpointInDoubtReason.AmbiguousAfterWrite : CheckpointInDoubtReason.VerificationUnavailable,
            cause);
    }

    /// <summary>
    ///     The identity a re-read must match for us to claim our own commit: the exact resulting control-plane token
    ///     (generation, revision, Active) AND the payload identity we wrote (position + processed count). A concurrent
    ///     winner with a different payload will not match, so it is reported InDoubt rather than a false success.
    /// </summary>
    public static Func<CheckpointSlot, bool> CommittedByExactResult(
        long expectedGeneration,
        long resultingRevision,
        CheckpointLifecycle resultingLifecycle,
        string lastSortableUniqueId,
        long eventsProcessed) =>
        slot => slot.Exists
            && slot.Generation == expectedGeneration
            && string.Equals(slot.Revision, resultingRevision.ToString(), StringComparison.Ordinal)
            && slot.Lifecycle == resultingLifecycle
            && slot.Record is { } record
            && string.Equals(record.LastSortableUniqueId, lastSortableUniqueId, StringComparison.Ordinal)
            && record.EventsProcessed == eventsProcessed;

    /// <summary>
    ///     Confirms a normal-persist / rebuilt-commit whose response was lost: the row is Active at the resulting
    ///     generation AND carries our exact payload identity (position + processed count). Shared by every provider's
    ///     write-in-doubt resolution so the identity rule lives in one place.
    /// </summary>
    public static Func<CheckpointSlot, bool> CommittedActiveByPayload(
        long resultingGeneration, string lastSortableUniqueId, long eventsProcessed) =>
        slot => slot.IsActive && slot.Generation == resultingGeneration && slot.Record is { } r
            && string.Equals(r.LastSortableUniqueId, lastSortableUniqueId, StringComparison.Ordinal)
            && r.EventsProcessed == eventsProcessed;

    /// <summary>
    ///     Confirms an invalidate (tombstone) whose response was lost by its resulting (generation, revision) — only an
    ///     invalidate from the exact Active token we observed can produce it. For stores with an opaque per-mutation token
    ///     (e.g. an ETag), pass <paramref name="resultingRevision" /> as a negative value to match on generation alone.
    /// </summary>
    public static Func<CheckpointSlot, bool> CommittedTombstoneByExact(long resultingGeneration, long resultingRevision) =>
        slot => slot.IsTombstoned && slot.Generation == resultingGeneration
            && (resultingRevision < 0 || string.Equals(slot.Revision, resultingRevision.ToString(), StringComparison.Ordinal));

    /// <summary>
    ///     Provider-shared resolution of a normal-persist / rebuilt-commit whose response was lost: a bounded re-read (via
    ///     the provider's <paramref name="reread" />) that confirms our exact resulting generation + Active + payload
    ///     identity reports Committed, else typed retryable InDoubt. Keeps the resolution shape in one place across stores.
    /// </summary>
    public static Task<CheckpointCasOutcome> ResolveActiveWriteAsync(
        Func<CancellationToken, Task<ResultBox<CheckpointSlot>>> reread,
        long resultingGeneration, string lastSortableUniqueId, long eventsProcessed, Exception cause,
        TimeSpan? verificationBudget = null) =>
        ResolveAsync(reread, CommittedActiveByPayload(resultingGeneration, lastSortableUniqueId, eventsProcessed),
            DefaultVerificationAttempts, cause, verificationBudget);

    /// <summary>
    ///     Provider-shared resolution of an invalidate (tombstone) whose response was lost: a bounded re-read that confirms
    ///     Tombstoned at the resulting (generation, revision) reports Committed, else typed retryable InDoubt. Pass a
    ///     negative <paramref name="resultingRevision" /> for stores with an opaque token (match on generation alone).
    /// </summary>
    public static Task<CheckpointCasOutcome> ResolveTombstoneWriteAsync(
        Func<CancellationToken, Task<ResultBox<CheckpointSlot>>> reread,
        long resultingGeneration, long resultingRevision, Exception cause,
        TimeSpan? verificationBudget = null) =>
        ResolveAsync(reread, CommittedTombstoneByExact(resultingGeneration, resultingRevision),
            DefaultVerificationAttempts, cause, verificationBudget);

    /// <summary>
    ///     The provider-shared write-failure classification (SEK-G20). A DETERMINISTIC pre-commit / schema failure (the
    ///     provider proves it never crossed the commit boundary) is ProviderFailure; otherwise the commit is unknown and is
    ///     resolved by the bounded re-read (Active + exact payload => Committed, else typed retryable InDoubt). One place so
    ///     the phase-marker rule cannot drift per provider.
    /// </summary>
    public static Task<CheckpointCasOutcome> ClassifyActiveWriteFailure(
        bool deterministicPreCommit,
        Exception cause,
        Func<CancellationToken, Task<ResultBox<CheckpointSlot>>> reread,
        long resultingGeneration, string lastSortableUniqueId, long eventsProcessed,
        TimeSpan? verificationBudget = null) =>
        deterministicPreCommit
            ? Task.FromResult(CheckpointCasOutcome.ProviderFailed(cause))
            : ResolveActiveWriteAsync(reread, resultingGeneration, lastSortableUniqueId, eventsProcessed, cause, verificationBudget);

    /// <summary>The tombstone (invalidate) counterpart of <see cref="ClassifyActiveWriteFailure" />.</summary>
    public static Task<CheckpointCasOutcome> ClassifyTombstoneWriteFailure(
        bool deterministicPreCommit,
        Exception cause,
        Func<CancellationToken, Task<ResultBox<CheckpointSlot>>> reread,
        long resultingGeneration, long resultingRevision,
        TimeSpan? verificationBudget = null) =>
        deterministicPreCommit
            ? Task.FromResult(CheckpointCasOutcome.ProviderFailed(cause))
            : ResolveTombstoneWriteAsync(reread, resultingGeneration, resultingRevision, cause, verificationBudget);
}
