using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Shared base for the WithResult and WithoutResult <see cref="GeneralSekibanExecutor" /> facades.
///     Holds the core executor, the actor accessor, the runtime descriptor, and the serialized commit surface
///     so those identical members are not duplicated across the two public API shapes.
/// </summary>
public abstract class GeneralSekibanExecutorBase : IExecutorRuntimeDescriptorProvider, ISerializedSekibanDcbExecutor
{
    protected readonly CoreGeneralSekibanExecutor _core;
    protected readonly IActorObjectAccessor ActorAccessor;

    /// <summary>
    ///     Creates the executor with the shared core implementation.
    /// </summary>
    protected GeneralSekibanExecutorBase(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null,
        IExecutedUserProvider? executedUserProvider = null)
    {
        ActorAccessor = actorAccessor;
        _core = new CoreGeneralSekibanExecutor(eventStore, actorAccessor, domainTypes, eventPublisher, executedUserProvider);
    }

    /// <summary>
    ///     This executor is whatever its accessor is. It has no runtime of its own — commands go wherever the accessor
    ///     sends them — so it asks, rather than claiming. An accessor that will not say leaves this Unknown, and the
    ///     production guard treats Unknown as unsafe, which is the only safe way to treat it.
    /// </summary>
    public ExecutorRuntimeDescriptor DescribeRuntime() =>
        SekibanDcbCapabilityResolver.DescribeExecutor(ActorAccessor);

    /// <summary>
    ///     Serialized tag-state retrieval, shared across both API shapes.
    /// </summary>
    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _core.GetSerializableTagStateAsync(tagStateId);

    /// <summary>
    ///     Serialized conditional commit, shared across both API shapes.
    /// </summary>
    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _core.CommitSerializableEventsAsync(request, cancellationToken);
}
