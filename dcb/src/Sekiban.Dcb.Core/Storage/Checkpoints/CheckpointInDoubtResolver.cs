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
        // Committed. ANY other outcome — a read that succeeds but does not show our write, OR a re-read that cannot
        // complete at all (the authority is unreachable/timing out) — leaves the commit UNKNOWN and MUST remain typed
        // retryable InDoubt, preserving the original safe cause/token. It is NEVER downgraded to ProviderFailure here:
        // unreadable authority after a possible commit does not establish a known pre-commit failure. (A deterministic
        // pre-commit / schema / preflight failure is classified ProviderFailure by the PROVIDER, before it calls this.)
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
                continue; // an unreadable authority does not prove non-commit; keep within budget, then InDoubt
            }
            if (read.IsSuccess && committedByUs(read.GetValue()))
            {
                return CheckpointCasOutcome.Committed(read.GetValue());
            }
        }
        return CheckpointCasOutcome.Doubt(cause);
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
}
