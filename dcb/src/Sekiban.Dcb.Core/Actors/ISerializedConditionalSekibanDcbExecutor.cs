using ResultBoxes;
using Sekiban.Dcb.Commands;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     OPTIONAL, additive WASM-boundary capability for conditional (unique-key) single-event append. Separate from
///     <see cref="ISerializedSekibanDcbExecutor" /> so implementors of the existing serialized contract keep compiling
///     untouched; feature-detected with <c>is ISerializedConditionalSekibanDcbExecutor</c>.
///     Fails closed (before EventId allocation / serialization / store call) with <c>ConditionNotSupportedException</c>
///     when the resolved store cannot enforce the condition.
/// </summary>
public interface ISerializedConditionalSekibanDcbExecutor
{
    Task<ResultBox<SerializedConditionalCommitResult>> CommitSerializableEventConditionallyAsync(
        SerializedConditionalCommitRequest request,
        CancellationToken cancellationToken = default);
}
