using ResultBoxes;
using Sekiban.Dcb.Capabilities;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     OPTIONAL, additive capability an event store may implement alongside <see cref="IEventStore" /> to offer
///     single-event conditional (unique-idempotency-key) append. It is feature-detected the same way the other optional
///     store capabilities are (<c>is IStreamingSerializableEventStore</c> etc.) and is NEVER a member of
///     <see cref="IEventStore" /> — external implementors keep compiling untouched.
///     A store that implements this MUST also implement <see cref="IWriteConditionCapabilityProvider" /> and list
///     <see cref="WriteConditionKind.SingleEventUniqueKey" />, so the runtime capability probe and the actual cast agree
///     (there is no default pass-through: not implementing the interface means the capability is not supported).
///     Contract of <see cref="AppendIfUniqueAsync" />:
///     <list type="bullet">
///         <item>first claim of the key → write the event, persist the claim, return a receipt with
///             <see cref="ConditionalAppendStatus.Appended" />;</item>
///         <item>same key, same operation fingerprint → write NOTHING, return the ORIGINAL winner's receipt with
///             <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" />;</item>
///         <item>same key, different fingerprint → write nothing, return <c>ResultBox.Error</c> carrying
///             <see cref="KeyReuseConflictException" />.</item>
///     </list>
///     The store guarantees at most ONE durable claim per key; making an external side effect exactly-once still needs an
///     outbox/idempotency layer on top.
/// </summary>
public interface IConditionalEventStore
{
    Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken = default);
}
