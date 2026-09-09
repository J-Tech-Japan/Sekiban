using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Orleans;

/// <summary>Arguments shared by the two Orleans executor facade constructors.</summary>
internal sealed class OrleansDcbExecutorConstructionInputs
{
    internal OrleansDcbExecutorConstructionInputs(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IServiceIdProvider? serviceIdProvider,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy,
        ExecutorSizeGateOptions? executorSizeGateOptions)
    {
        ClusterClient = clusterClient;
        EventStore = eventStore;
        DomainTypes = domainTypes;
        EventPublisher = eventPublisher;
        ServiceIdProvider = serviceIdProvider;
        ExecutedUserProvider = executedUserProvider;
        SortableUniqueIdGenerator = sortableUniqueIdGenerator;
        SortableUniqueIdSeedCoordinator = sortableUniqueIdSeedCoordinator;
        SortableUniqueIdWaitPolicy = sortableUniqueIdWaitPolicy;
        ExecutorSizeGateOptions = executorSizeGateOptions;
    }

    internal IClusterClient ClusterClient { get; }

    internal IEventStore EventStore { get; }

    internal DcbDomainTypes DomainTypes { get; }

    internal IEventPublisher? EventPublisher { get; }

    internal IServiceIdProvider? ServiceIdProvider { get; }

    internal IExecutedUserProvider? ExecutedUserProvider { get; }

    internal ISortableUniqueIdGenerator SortableUniqueIdGenerator { get; }

    internal SortableUniqueIdSeedCoordinator SortableUniqueIdSeedCoordinator { get; }

    internal SortableUniqueIdWaitPolicy SortableUniqueIdWaitPolicy { get; }

    internal ExecutorSizeGateOptions? ExecutorSizeGateOptions { get; }
}

/// <summary>
///     Shared validated Orleans dependencies and facade-specific general executor.
///     The generic result keeps the ResultBox and exception assemblies independent while sharing construction.
/// </summary>
internal sealed class OrleansDcbExecutorConstruction<TGeneralExecutor>
{
    internal OrleansDcbExecutorConstruction(
        IActorObjectAccessor actorAccessor,
        IClusterClient clusterClient,
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        TGeneralExecutor generalExecutor,
        OrleansProjectionQueryExecutor queryExecutor,
        IServiceIdProvider serviceIdProvider,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy)
    {
        ActorAccessor = actorAccessor;
        ClusterClient = clusterClient;
        DomainTypes = domainTypes;
        EventStore = eventStore;
        GeneralExecutor = generalExecutor;
        QueryExecutor = queryExecutor;
        ServiceIdProvider = serviceIdProvider;
        SortableUniqueIdWaitPolicy = sortableUniqueIdWaitPolicy;
    }

    internal static OrleansDcbExecutorConstruction<TGeneralExecutor> Create(
        OrleansDcbExecutorConstructionInputs inputs,
        Func<OrleansDcbExecutorConstructionInputs, IActorObjectAccessor, IServiceIdProvider,
            SortableUniqueIdWaitPolicy, TGeneralExecutor> createGeneralExecutor)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(createGeneralExecutor);

        var clusterClient = inputs.ClusterClient ?? throw new ArgumentNullException(nameof(inputs.ClusterClient));
        var eventStore = inputs.EventStore ?? throw new ArgumentNullException(nameof(inputs.EventStore));
        var domainTypes = inputs.DomainTypes ?? throw new ArgumentNullException(nameof(inputs.DomainTypes));
        var serviceIdProvider = inputs.ServiceIdProvider ?? new DefaultServiceIdProvider();
        var waitPolicy = inputs.SortableUniqueIdWaitPolicy ??
                         throw new ArgumentNullException(nameof(inputs.SortableUniqueIdWaitPolicy));
        var actorAccessor = new OrleansActorObjectAccessor(clusterClient, eventStore, domainTypes, serviceIdProvider);
        var queryExecutor = new OrleansProjectionQueryExecutor(
            clusterClient,
            domainTypes,
            serviceIdProvider,
            waitPolicy);
        var generalExecutor = createGeneralExecutor(inputs, actorAccessor, serviceIdProvider, waitPolicy);

        return new OrleansDcbExecutorConstruction<TGeneralExecutor>(
            actorAccessor,
            clusterClient,
            domainTypes,
            eventStore,
            generalExecutor,
            queryExecutor,
            serviceIdProvider,
            waitPolicy);
    }

    internal IActorObjectAccessor ActorAccessor { get; }

    internal IClusterClient ClusterClient { get; }

    internal DcbDomainTypes DomainTypes { get; }

    internal IEventStore EventStore { get; }

    internal TGeneralExecutor GeneralExecutor { get; }

    internal OrleansProjectionQueryExecutor QueryExecutor { get; }

    internal IServiceIdProvider ServiceIdProvider { get; }

    internal SortableUniqueIdWaitPolicy SortableUniqueIdWaitPolicy { get; }
}
