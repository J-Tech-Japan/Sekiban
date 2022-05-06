using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
namespace CustomerDomainContext.AggregateEventSubscribers;

public class ClientDeletedSubscriber : AggregateEventSubscriberBase<ClientDeleted>
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;

    public ClientDeletedSubscriber(AggregateCommandExecutor aggregateCommandExecutor) =>
        _aggregateCommandExecutor = aggregateCommandExecutor;

    public override async Task SubscribeAggregateEventAsync(ClientDeleted ev)
    {
        await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, DeleteLoyaltyPoint>(
            new DeleteLoyaltyPoint(ev.ClientId),
            ev.GetCallHistoriesIncludesItself());
    }
}
