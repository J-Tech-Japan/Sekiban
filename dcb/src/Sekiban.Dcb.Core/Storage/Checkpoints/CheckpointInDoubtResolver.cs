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
    public static async Task<CheckpointCasOutcome> ResolveAsync(
        Func<CancellationToken, Task<ResultBox<CheckpointSlot>>> reread,
        Func<CheckpointSlot, bool> committedByUs,
        int maxAttempts,
        Exception cause)
    {
        ArgumentNullException.ThrowIfNull(reread);
        ArgumentNullException.ThrowIfNull(committedByUs);

        // The caller invokes this ONLY when the write already crossed a commit-capable boundary (a dispatch/transport
        // failure), so its commit is genuinely unknown. A bounded independent re-read that confirms our exact write ->
        // Committed. ANY other outcome leaves the commit UNKNOWN and MUST remain typed retryable InDoubt, preserving the
        // original safe cause/token. It is NEVER downgraded to ProviderFailure here (a deterministic pre-commit / schema
        // failure is classified ProviderFailure by the PROVIDER, before it calls this). The closed InDoubt reason records
        // WHY the winner is unknown:
        //   - at least one re-read SUCCEEDED but none confirmed our write => AmbiguousAfterWrite;
        //   - EVERY bounded re-read failed/timed out (authority unreachable) => VerificationUnavailable.
        var anyReadSucceeded = false;
        for (var attempt = 0; attempt < Math.Max(1, maxAttempts); attempt++)
        {
            ResultBox<CheckpointSlot> read;
            try
            {
                // Independent of the caller's (possibly cancelled) token — the verification budget is caller-independent.
                read = await reread(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                continue; // an unreadable authority does not prove non-commit; keep within budget
            }
            if (!read.IsSuccess)
            {
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
        long resultingGeneration, string lastSortableUniqueId, long eventsProcessed, Exception cause) =>
        ResolveAsync(reread, CommittedActiveByPayload(resultingGeneration, lastSortableUniqueId, eventsProcessed), 3, cause);

    /// <summary>
    ///     Provider-shared resolution of an invalidate (tombstone) whose response was lost: a bounded re-read that confirms
    ///     Tombstoned at the resulting (generation, revision) reports Committed, else typed retryable InDoubt. Pass a
    ///     negative <paramref name="resultingRevision" /> for stores with an opaque token (match on generation alone).
    /// </summary>
    public static Task<CheckpointCasOutcome> ResolveTombstoneWriteAsync(
        Func<CancellationToken, Task<ResultBox<CheckpointSlot>>> reread,
        long resultingGeneration, long resultingRevision, Exception cause) =>
        ResolveAsync(reread, CommittedTombstoneByExact(resultingGeneration, resultingRevision), 3, cause);
}
