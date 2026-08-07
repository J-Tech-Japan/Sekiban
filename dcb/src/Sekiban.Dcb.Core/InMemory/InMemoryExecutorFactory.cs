using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     Factory helper shared by the WithResult and WithoutResult <see cref="InMemoryDcbExecutor" /> facades.
///     Builds the in-memory accessor and the flavor-specific inner executor in one place so the construction
///     sequence is not duplicated across packages.
/// </summary>
internal static class InMemoryExecutorFactory
{
    public static (InMemoryObjectAccessor Accessor, TInner Inner) Create<TInner>(
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IExecutedUserProvider? executedUserProvider,
        Func<IEventStore, InMemoryObjectAccessor, DcbDomainTypes, IEventPublisher, IExecutedUserProvider?, TInner> createInner)
    {
        var accessor = new InMemoryObjectAccessor(eventStore, domainTypes);
        var publisher = new InMemoryMultiProjectionEventPublisher(accessor);
        var inner = createInner(eventStore, accessor, domainTypes, publisher, executedUserProvider);
        return (accessor, inner);
    }
}
