using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Factory helper shared by the WithResult and WithoutResult <see cref="OrleansDcbExecutor" /> facades.
///     Builds the Orleans actor accessor and the flavor-specific inner executor in one place so the construction
///     sequence is not duplicated across packages.
/// </summary>
internal static class OrleansExecutorFactory
{
    public static (IActorObjectAccessor ActorAccessor, IServiceIdProvider ServiceIdProvider, TInner Inner) Create<TInner>(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IServiceIdProvider? serviceIdProvider,
        IExecutedUserProvider? executedUserProvider,
        Func<IEventStore, IActorObjectAccessor, DcbDomainTypes, IEventPublisher?, IExecutedUserProvider?, TInner> createInner)
    {
        var provider = serviceIdProvider ?? new DefaultServiceIdProvider();
        var actorAccessor = new OrleansActorObjectAccessor(clusterClient, eventStore, domainTypes, provider);
        var inner = createInner(eventStore, actorAccessor, domainTypes, eventPublisher, executedUserProvider);
        return (actorAccessor, provider, inner);
    }
}
