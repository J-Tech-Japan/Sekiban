using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TagConsistencyFence;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Internal construction state shared by the ResultBox and exception facade assemblies.
///     Keeping the state transfer here lets both public facades retain their historical constructors without
///     duplicating the size-gate construction bodies.
/// </summary>
internal sealed class GeneralSekibanExecutorConstruction
{
    internal GeneralSekibanExecutorConstruction(
        IActorObjectAccessor actorAccessor,
        CoreGeneralSekibanExecutor core)
    {
        ActorAccessor = actorAccessor;
        Core = core;
    }

    internal static GeneralSekibanExecutorConstruction WithFence(
        IEventStore store, IActorObjectAccessor accessor, DcbDomainTypes domain,
        TagConsistencyFenceOptions? fence, ExecutorSizeGateOptions? gate,
        IEventPublisher? publisher, IExecutedUserProvider? user, IServiceIdProvider? serviceId) =>
        WithFence(store, accessor, domain, fence, gate, publisher, user,
            serviceId ?? new DefaultServiceIdProvider(),
            ProcessSharedSortableUniqueIdServices.Generator, ProcessSharedSortableUniqueIdServices.SeedCoordinator);

    internal static GeneralSekibanExecutorConstruction WithFence(
        IEventStore store, IActorObjectAccessor accessor, DcbDomainTypes domain,
        TagConsistencyFenceOptions? fence, ExecutorSizeGateOptions? gate,
        IEventPublisher? publisher, IExecutedUserProvider? user, IServiceIdProvider serviceId,
        ISortableUniqueIdGenerator generator, SortableUniqueIdSeedCoordinator seed) =>
        new(accessor, CoreGeneralSekibanExecutorFactory.CreateWithFence(
            store, accessor, domain, fence, gate, publisher, user, generator, seed, serviceId));

    internal IActorObjectAccessor ActorAccessor { get; }

    internal CoreGeneralSekibanExecutor Core { get; }
}

#pragma warning disable CS0618

/// <summary>Shared in-memory wiring for both public facades and their testing subclasses.</summary>
internal sealed record InMemoryExecutorConstruction(
    DcbDomainTypes DomainTypes, IEventStore EventStore, InMemoryObjectAccessor Accessor,
    GeneralSekibanExecutorConstruction General)
{
    internal static InMemoryExecutorConstruction WithFence(
        DcbDomainTypes domain, IEventStore store, TagConsistencyFenceOptions fence,
        ExecutorSizeGateOptions? gate, IExecutedUserProvider? user, IServiceIdProvider? serviceId) =>
        WithFence(domain, store, fence, gate, user, serviceId ?? new DefaultServiceIdProvider(),
            ProcessSharedSortableUniqueIdServices.Generator, ProcessSharedSortableUniqueIdServices.SeedCoordinator);

    internal static InMemoryExecutorConstruction WithFence(
        DcbDomainTypes domain, IEventStore store, TagConsistencyFenceOptions fence,
        ExecutorSizeGateOptions? gate, IExecutedUserProvider? user, IServiceIdProvider serviceId,
        ISortableUniqueIdGenerator generator, SortableUniqueIdSeedCoordinator seed)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(store);
        var accessor = new InMemoryObjectAccessor(store, domain);
        return new(domain, store, accessor, GeneralSekibanExecutorConstruction.WithFence(
            store, accessor, domain, fence, gate, new InMemoryMultiProjectionEventPublisher(accessor),
            user, serviceId, generator, seed));
    }
}

#pragma warning restore CS0618
