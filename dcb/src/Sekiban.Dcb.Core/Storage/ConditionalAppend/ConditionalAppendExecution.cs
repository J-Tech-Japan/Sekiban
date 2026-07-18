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
///     only two primitives — how to durably write the claim event under a deterministic id (using its native uniqueness
///     primitive) and how to read the committed winner back — and this method produces the identical observable outcome
///     machine everywhere: <see cref="ConditionalAppendStatus.Appended" /> /
///     <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" /> (same fingerprint, original winner's receipt)
///     / <see cref="ConditionalAppendStatus.KeyReuseConflict" />, failing closed on an unsupported payload BEFORE any
///     write.
///     Fingerprints are recomputed from persisted event CONTENT (never stored separately), so no schema change is needed:
///     a same-operation retry recomputes an identical fingerprint from the stored winner; a different operation under the
///     same key recomputes a different one and is a key-reuse conflict.
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
        catch (Exception ex)
        {
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
        SerializableEvent? existing;
        try
        {
            existing = await readCommittedWinner(deterministicId, cancellationToken);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ConditionalAppendReceipt>(ex);
        }

        if (existing is null)
        {
            // The conflict said the id exists, but no committed winner could be read back — an in-doubt/uncommitted
            // state. Fail (do NOT report AlreadyCommitted); the caller may retry, which converges once it commits.
            return ResultBox.Error<ConditionalAppendReceipt>(
                new InvalidOperationException(
                    $"Conditional append on '{providerName}' hit a claim conflict but could not read back a committed "
                    + "winner to verify it; the claim is in-doubt. Retry."));
        }

        var existingFingerprintResult = OperationFingerprint.ComputeCanonical(
            serviceId,
            request.IdempotencyKey,
            eventTypes,
            existing.EventPayloadName,
            existing.Payload,
            existing.Tags);
        if (!existingFingerprintResult.IsSuccess)
        {
            return ResultBox.Error<ConditionalAppendReceipt>(existingFingerprintResult.GetException());
        }

        var existingFingerprint = existingFingerprintResult.GetValue();
        if (string.Equals(existingFingerprint, attemptFingerprint, StringComparison.Ordinal))
        {
            // Same operation: return the ORIGINAL winner's receipt; nothing was written this attempt.
            return ResultBox.FromValue(
                new ConditionalAppendReceipt(
                    ConditionalAppendStatus.AlreadyCommittedSameOperation,
                    existing.Id,
                    existing.SortableUniqueIdValue,
                    existingFingerprint));
        }

        // Same key, different operation: key-reuse conflict. Preserve the provider exception as the diagnostic cause ONLY
        // when one actually occurred (a conflict discovered purely by read carries none).
        return ResultBox.Error<ConditionalAppendReceipt>(
            new KeyReuseConflictException(existingFingerprint, attemptFingerprint, providerName, outcome.ConflictException));
    }
}
