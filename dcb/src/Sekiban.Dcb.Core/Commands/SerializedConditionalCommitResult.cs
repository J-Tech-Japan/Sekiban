using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     NEW, versioned serialized-commit result for the WASM boundary's conditional append. Reports the full outcome
///     machine: <see cref="Status" /> is Appended or AlreadyCommittedSameOperation on success (with the durable receipt
///     fields), while KeyReuseConflict / ConditionNotSupported are surfaced as errors (ResultBox.Error / guarded throw),
///     never as this result. <see cref="WrittenEvents" /> is the single written event on Appended, and empty on an
///     already-committed retry (nothing was written that attempt).
/// </summary>
public record SerializedConditionalCommitResult(
    int Version,
    ConditionalAppendStatus Status,
    Guid WinnerEventId,
    string WinnerSortableUniqueId,
    string OperationFingerprint,
    IReadOnlyList<SerializableEvent> WrittenEvents,
    TimeSpan Duration)
{
    public const int CurrentVersion = 1;
}
