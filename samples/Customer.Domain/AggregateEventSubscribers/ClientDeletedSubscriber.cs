using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;
namespace Customer.Domain.AggregateEventSubscribers;

public class ClientDeletedSubscriber : AggregateEventSubscriberBase<ClientDeleted>
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;

    public ClientDeletedSubscriber(IAggregateCommandExecutor aggregateCommandExecutor)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
    }

    public override async Task SubscribeAggregateEventAsync(AggregateEvent<ClientDeleted> ev)
    {
        await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, DeleteLoyaltyPoint>(
            new DeleteLoyaltyPoint(ev.AggregateId),
            ev.GetCallHistoriesIncludesItself());
    }
}
