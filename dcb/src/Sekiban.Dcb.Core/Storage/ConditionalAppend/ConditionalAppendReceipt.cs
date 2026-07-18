namespace Sekiban.Dcb.Storage;

/// <summary>
///     The durable receipt of a won (or already-won) unique-key claim. Returned on the success statuses
///     <see cref="ConditionalAppendStatus.Appended" /> and <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" />.
///     It always names the WINNER — the identity that actually holds the durable claim — so a same-operation retry gets
///     back the original winner's <see cref="WinnerEventId" />/<see cref="WinnerSortableUniqueId" />, not the ids the
///     retry would have allocated. The <see cref="OperationFingerprint" /> is the canonical fingerprint persisted with the
///     claim (see <see cref="Sekiban.Dcb.Storage.OperationFingerprint" />); raw idempotency keys are never carried here.
/// </summary>
public sealed record ConditionalAppendReceipt(
    ConditionalAppendStatus Status,
    Guid WinnerEventId,
    string WinnerSortableUniqueId,
    string OperationFingerprint)
{
    /// <summary>True when the receipt describes an already-committed same-operation retry (nothing written this attempt).</summary>
    public bool WasAlreadyCommitted => Status == ConditionalAppendStatus.AlreadyCommittedSameOperation;
}
