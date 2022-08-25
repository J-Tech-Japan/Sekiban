using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Commands;
namespace CustomerWithTenantAddonDomainContext.AggregateEventSubscribers;

public class ClientDeletedSubscriber : AggregateEventSubscriberBase<ClientDeleted>
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;

    public ClientDeletedSubscriber(IAggregateCommandExecutor aggregateCommandExecutor) =>
        _aggregateCommandExecutor = aggregateCommandExecutor;

    public override async Task SubscribeAggregateEventAsync(AggregateEvent<ClientDeleted> ev)
    {
        await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, DeleteLoyaltyPoint>(
            new DeleteLoyaltyPoint(ev.AggregateId),
            ev.GetCallHistoriesIncludesItself());
    }
}
