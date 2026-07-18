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
    public static async Task<ResultBox<ConditionalAppendReceipt>> RunAsync(
        ConditionalAppendRequest request,
        string serviceId,
        IEventTypes eventTypes,
        string providerName,
        Func<Guid, SerializableEvent, CancellationToken, Task<ConditionalWriteOutcome>> tryWriteClaim,
        Func<Guid, CancellationToken, Task<SerializableEvent?>> readCommittedWinner,
        Func<SerializableEvent, CancellationToken, Task>? ensureCommittedAsync = null,
        CancellationToken cancellationToken = default)
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

        ConditionalWriteOutcome outcome;
        try
        {
            outcome = await tryWriteClaim(deterministicId, claimEvent, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // Ambiguous: the write may have durably committed before the cancellation/timeout. Resolve it authoritatively
            // by reading the winner back (with a fresh token, since the caller's may be cancelled) and classifying.
            return await ResolveByReadbackAsync(
                serviceId, request.IdempotencyKey, eventTypes, providerName, deterministicId, attemptFingerprint,
                readCommittedWinner, ensureCommittedAsync, ex,
                ConditionalAppendInDoubtException.ReasonAmbiguousAfterWrite, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Any other write failure is surfaced as-is (permanent/unexpected); it is not classified as retryable in-doubt.
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
            ConditionalAppendInDoubtException.ReasonWinnerUnreadable, cancellationToken);
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
        string unreadableReason,
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
                new ConditionalAppendInDoubtException(providerName, serviceId, deterministicId, unreadableReason, cause ?? ex));
        }

        if (existing is null)
        {
            // The conflict/ambiguity said the id may exist, but no committed winner could be read back. Do NOT report
            // AlreadyCommitted; the caller may retry, which converges once it commits.
            return ResultBox.Error<ConditionalAppendReceipt>(
                new ConditionalAppendInDoubtException(providerName, serviceId, deterministicId, unreadableReason, cause));
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
            catch (Exception ex)
            {
                return ResultBox.Error<ConditionalAppendReceipt>(
                    new ConditionalAppendInDoubtException(
                        providerName, serviceId, deterministicId,
                        ConditionalAppendInDoubtException.ReasonCommittedStateUnverified, ex));
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
