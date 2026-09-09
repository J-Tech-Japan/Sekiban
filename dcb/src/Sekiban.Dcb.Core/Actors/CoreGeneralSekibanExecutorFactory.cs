using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Actors;

internal static class CoreGeneralSekibanExecutorFactory
{
    internal static CoreGeneralSekibanExecutor CreateWithGate(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        ExecutorSizeGateOptions options,
        IEventPublisher? eventPublisher,
        IExecutedUserProvider? executedUserProvider) =>
        new(eventStore, actorAccessor, domainTypes, options, eventPublisher, executedUserProvider);

    internal static CoreGeneralSekibanExecutor CreateWithServices(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        IServiceIdProvider serviceIdProvider,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy,
        ExecutorSizeGateOptions? options) =>
        new(
            eventStore,
            actorAccessor,
            domainTypes,
            eventPublisher,
            executedUserProvider,
            sortableUniqueIdGenerator,
            sortableUniqueIdSeedCoordinator,
            serviceIdProvider,
            sortableUniqueIdWaitPolicy,
            options);
}
