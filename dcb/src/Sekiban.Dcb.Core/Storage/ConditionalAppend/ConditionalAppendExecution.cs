using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     The outcome of a provider's attempt to durably write the single claim event under the deterministic id.
/// </summary>
/// <param name="Written">True iff the event was durably written by THIS attempt (the caller won the key).</param>
/// <param name="ConflictException">
///     The real provider exception that revealed a pre-existing claim (SQLSTATE 23505, Cosmos 409,
///     ConditionalCheckFailed, …), or null when the conflict was discovered by read with no provider error. Preserved
///     verbatim so it can be attached as the diagnostic cause — never fabricated.
/// </param>
public readonly record struct ConditionalWriteOutcome(bool Written, Exception? ConflictException)
{
    public static ConditionalWriteOutcome Wrote() => new(true, null);
    public static ConditionalWriteOutcome Conflict(Exception? providerException) => new(false, providerException);
}

/// <summary>
///     The single implementation of the SEK-G15 unique-append semantics shared by every provider. A provider supplies
///     three primitives — how to durably write the claim event under a deterministic id (using its native uniqueness
///     primitive), how to read the committed winner back, and (optionally) how to bring the winner's CONTRACTED committed
///     state to convergence before its receipt is returned — and this method produces the identical observable outcome
///     machine everywhere: <see cref="ConditionalAppendStatus.Appended" /> /
///     <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" /> (same fingerprint, original winner's receipt)
///     / <see cref="ConditionalAppendStatus.KeyReuseConflict" />, failing closed on an unsupported payload BEFORE any
///     write, and raising a typed RETRYABLE <see cref="ConditionalAppendInDoubtException" /> when the outcome cannot be
///     resolved (winner unreadable, ambiguous after cancellation/timeout, committed state unverified).
///     Fingerprints are recomputed from persisted event CONTENT (never stored separately), so no schema change is needed.
///     The <c>ensureCommittedAsync</c> gate closes the event/tag (or event/materialization) roll-forward window: a
///     same-operation retry must NOT report AlreadyCommitted from a bare event-only conflict on a store whose event and
///     tag rows are not written atomically — the required rows are idempotently repaired/verified first.
/// </summary>
public static class ConditionalAppendExecution
{
    /// <summary>The default independent budget for authoritative post-ambiguity verification (winner read + committed gate).</summary>
    public static readonly TimeSpan DefaultVerificationBudget = TimeSpan.FromSeconds(30);

    public static async Task<ResultBox<ConditionalAppendReceipt>> RunAsync(
        ConditionalAppendRequest request,
        string serviceId,
        IEventTypes eventTypes,
        string providerName,
        Func<Guid, SerializableEvent, CancellationToken, Task<ConditionalWriteOutcome>> tryWriteClaim,
        Func<Guid, CancellationToken, Task<SerializableEvent?>> readCommittedWinner,
        Func<SerializableEvent, CancellationToken, Task>? ensureCommittedAsync = null,
        CancellationToken cancellationToken = default,
        TimeSpan? verificationBudget = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(eventTypes);
        ArgumentNullException.ThrowIfNull(tryWriteClaim);
        ArgumentNullException.ThrowIfNull(readCommittedWinner);

        string normalizedKey;
        try
        {
            normalizedKey = OperationFingerprint.NormalizeKey(request.IdempotencyKey);
        }
        catch (ArgumentException ex)
        {
            // A malformed key is a permanent bad request, not an in-doubt/retryable state.
            return ResultBox.Error<ConditionalAppendReceipt>(ex);
        }

        // Compute the attempt fingerprint (fail-closed on unsupported shape) BEFORE deriving an id or touching the store.
        var attemptFingerprintResult = OperationFingerprint.ComputeCanonical(
            serviceId,
            request.IdempotencyKey,
            eventTypes,
            request.Event.EventPayloadName,
            request.Event.Payload,
            request.Event.Tags);
        if (!attemptFingerprintResult.IsSuccess)
        {
            return ResultBox.Error<ConditionalAppendReceipt>(attemptFingerprintResult.GetException());
        }

        var attemptFingerprint = attemptFingerprintResult.GetValue();
        var deterministicId = ConditionalAppendIdentity.DeriveEventId(serviceId, normalizedKey);

        // The claim event is the caller's event stored under the deterministic id (its own random id is discarded so the
        // storage identity is a pure function of the key). Payload/tags/metadata/sortable-id are preserved.
        var claimEvent = request.Event with { Id = deterministicId };

        var budget = verificationBudget ?? DefaultVerificationBudget;

        ConditionalWriteOutcome outcome;
        try
        {
            outcome = await tryWriteClaim(deterministicId, claimEvent, cancellationToken);
        }
        catch (PostCommitResponseLostException ex)
        {
            // The provider KNOWS the claim committed durably but the response was lost. Resolve authoritatively via a
            // BOUNDED, caller-independent verification: AlreadyCommitted on proof, else typed AmbiguousAfterWrite with the
            // original transport/cancellation cause — never the raw transport exception.
            return await ResolveAmbiguousAfterWriteAsync(
                serviceId, request.IdempotencyKey, eventTypes, providerName, deterministicId, attemptFingerprint,
                readCommittedWinner, ensureCommittedAsync, ex.OriginalCause, budget);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // Ambiguous: the write may have committed before the cancellation/timeout. Resolve it the same way — a bounded
            // independent verification (the caller's token may be cancelled, so it must NOT gate the verification).
            return await ResolveAmbiguousAfterWriteAsync(
                serviceId, request.IdempotencyKey, eventTypes, providerName, deterministicId, attemptFingerprint,
                readCommittedWinner, ensureCommittedAsync, ex, budget);
        }
        catch (Exception ex)
        {
            // Any other write failure is surfaced as-is (permanent/unexpected pre-commit); not classified as in-doubt.
            return ResultBox.Error<ConditionalAppendReceipt>(ex);
        }

        if (outcome.Written)
        {
            return ResultBox.FromValue(
                new ConditionalAppendReceipt(
                    ConditionalAppendStatus.Appended,
                    deterministicId,
                    claimEvent.SortableUniqueIdValue,
                    attemptFingerprint));
        }

        // A conflict: the key is already claimed. Read the committed winner back and CLASSIFY by fingerprint — a bare
        // provider conflict (409 / 23505 / ConditionalCheckFailed) is never on its own proof of a same-operation success.
        return await ResolveByReadbackAsync(
            serviceId, request.IdempotencyKey, eventTypes, providerName, deterministicId, attemptFingerprint,
            readCommittedWinner, ensureCommittedAsync, outcome.ConflictException,
            ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict, cancellationToken);
    }

    /// <summary>
    ///     Resolves a post-write ambiguity (durable commit whose response was lost, or a cancellation/timeout that may
    ///     have committed) with a BOUNDED, caller-independent verification: an authoritative winner read + fingerprint +
    ///     committed-state gate, all under a fresh budget token (never the caller's, which may already be cancelled, and
    ///     never unbounded). On proof it returns the original winner's receipt (AlreadyCommitted) on THIS call; if the
    ///     bounded verification cannot complete (budget exceeded, no readable winner) it returns typed
    ///     <see cref="ConditionalAppendInDoubtReason.AmbiguousAfterWrite" /> preserving the original cause (and its exact
    ///     CancellationToken when the cause was a cancellation). A disagreeing committed row is still non-retryable
    ///     corruption; a different fingerprint is still a key-reuse conflict.
    /// </summary>
    private static async Task<ResultBox<ConditionalAppendReceipt>> ResolveAmbiguousAfterWriteAsync(
        string serviceId,
        string idempotencyKey,
        IEventTypes eventTypes,
        string providerName,
        Guid deterministicId,
        string attemptFingerprint,
        Func<Guid, CancellationToken, Task<SerializableEvent?>> readCommittedWinner,
        Func<SerializableEvent, CancellationToken, Task>? ensureCommittedAsync,
        Exception cause,
        TimeSpan budget)
    {
        ConditionalAppendInDoubtException InDoubt() => ConditionalAppendInDoubtException.Create(
            providerName, serviceId, deterministicId, ConditionalAppendInDoubtReason.AmbiguousAfterWrite, cause);

        using var cts = new CancellationTokenSource(budget);
        SerializableEvent? existing;
        try
        {
            existing = await readCommittedWinner(deterministicId, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Bounded budget exceeded (or the read itself cancelled): unresolved — typed in-doubt, original cause preserved.
            return ResultBox.Error<ConditionalAppendReceipt>(InDoubt());
        }

        if (existing is null)
        {
            return ResultBox.Error<ConditionalAppendReceipt>(InDoubt());
        }

        var existingFingerprintResult = OperationFingerprint.ComputeCanonical(
            serviceId, idempotencyKey, eventTypes, existing.EventPayloadName, existing.Payload, existing.Tags);
        if (!existingFingerprintResult.IsSuccess)
        {
            return ResultBox.Error<ConditionalAppendReceipt>(existingFingerprintResult.GetException());
        }

        var existingFingerprint = existingFingerprintResult.GetValue();
        if (!string.Equals(existingFingerprint, attemptFingerprint, StringComparison.Ordinal))
        {
            return ResultBox.Error<ConditionalAppendReceipt>(
                new KeyReuseConflictException(existingFingerprint, attemptFingerprint, providerName, cause));
        }

        if (ensureCommittedAsync is not null)
        {
            try
            {
                await ensureCommittedAsync(existing, cts.Token);
            }
            catch (ConditionalAppendCommittedStateCorruptionException corruption)
            {
                return ResultBox.Error<ConditionalAppendReceipt>(corruption);
            }
            catch (Exception)
            {
                // The bounded committed-state gate could not complete (budget exceeded / transient): typed in-doubt.
                return ResultBox.Error<ConditionalAppendReceipt>(InDoubt());
            }
        }

        return ResultBox.FromValue(
            new ConditionalAppendReceipt(
                ConditionalAppendStatus.AlreadyCommittedSameOperation,
                existing.Id,
                existing.SortableUniqueIdValue,
                existingFingerprint));
    }

    /// <summary>
    ///     Reads the committed winner back and classifies: same fingerprint (after the committed-state gate) →
    ///     AlreadyCommitted; different → KeyReuseConflict; unreadable/unverifiable → typed retryable in-doubt.
    /// </summary>
    private static async Task<ResultBox<ConditionalAppendReceipt>> ResolveByReadbackAsync(
        string serviceId,
        string idempotencyKey,
        IEventTypes eventTypes,
        string providerName,
        Guid deterministicId,
        string attemptFingerprint,
        Func<Guid, CancellationToken, Task<SerializableEvent?>> readCommittedWinner,
        Func<SerializableEvent, CancellationToken, Task>? ensureCommittedAsync,
        Exception? cause,
        ConditionalAppendInDoubtReason unreadableReason,
        CancellationToken cancellationToken)
    {
        SerializableEvent? existing;
        try
        {
            existing = await readCommittedWinner(deterministicId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The winner could not even be read — in-doubt. Prefer the original conflict/cancellation cause when present.
            return ResultBox.Error<ConditionalAppendReceipt>(
                ConditionalAppendInDoubtException.Create(providerName, serviceId, deterministicId, unreadableReason, cause ?? ex));
        }

        if (existing is null)
        {
            // The conflict/ambiguity said the id may exist, but no committed winner could be read back. Do NOT report
            // AlreadyCommitted; the caller may retry, which converges once it commits.
            return ResultBox.Error<ConditionalAppendReceipt>(
                ConditionalAppendInDoubtException.Create(providerName, serviceId, deterministicId, unreadableReason, cause));
        }

        var existingFingerprintResult = OperationFingerprint.ComputeCanonical(
            serviceId,
            idempotencyKey,
            eventTypes,
            existing.EventPayloadName,
            existing.Payload,
            existing.Tags);
        if (!existingFingerprintResult.IsSuccess)
        {
            return ResultBox.Error<ConditionalAppendReceipt>(existingFingerprintResult.GetException());
        }

        var existingFingerprint = existingFingerprintResult.GetValue();
        if (!string.Equals(existingFingerprint, attemptFingerprint, StringComparison.Ordinal))
        {
            // Same key, different operation: key-reuse conflict. Preserve the provider exception as the diagnostic cause
            // ONLY when one actually occurred (a conflict discovered purely by read carries none).
            return ResultBox.Error<ConditionalAppendReceipt>(
                new KeyReuseConflictException(existingFingerprint, attemptFingerprint, providerName, cause));
        }

        // Same operation. Before returning the ORIGINAL winner's receipt, close the roll-forward window: bring the
        // contracted committed state (e.g. every required tag/materialization row) to convergence. On a store that writes
        // event and tags atomically this is a no-op; where it is not, a failure here is in-doubt (retryable), NOT a false
        // AlreadyCommitted.
        if (ensureCommittedAsync is not null)
        {
            try
            {
                await ensureCommittedAsync(existing, cancellationToken);
            }
            catch (ConditionalAppendCommittedStateCorruptionException corruption)
            {
                // A disagreeing (corrupt) committed row is NON-retryable and must never be overwritten — surface it as-is,
                // never reclassified to a retryable in-doubt.
                return ResultBox.Error<ConditionalAppendReceipt>(corruption);
            }
            catch (Exception ex)
            {
                // A transient/unresolved repair failure (e.g. exhausted retries) is retryable in-doubt.
                return ResultBox.Error<ConditionalAppendReceipt>(
                    ConditionalAppendInDoubtException.Create(
                        providerName, serviceId, deterministicId,
                        ConditionalAppendInDoubtReason.CommittedStateUnverified, ex));
            }
        }

        return ResultBox.FromValue(
            new ConditionalAppendReceipt(
                ConditionalAppendStatus.AlreadyCommittedSameOperation,
                existing.Id,
                existing.SortableUniqueIdValue,
                existingFingerprint));
    }
}
