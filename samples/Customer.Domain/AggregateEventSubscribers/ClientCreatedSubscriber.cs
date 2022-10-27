using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;
namespace Customer.Domain.AggregateEventSubscribers;

public class ClientCreatedSubscriber : AggregateEventSubscriberBase<ClientCreated>
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;

    public ClientCreatedSubscriber(IAggregateCommandExecutor aggregateCommandExecutor)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
    }

    public override async Task SubscribeAggregateEventAsync(AggregateEvent<ClientCreated> ev)
    {
        await _aggregateCommandExecutor.ExecCreateCommandAsync<LoyaltyPoint, CreateLoyaltyPoint>(
            new CreateLoyaltyPoint(ev.AggregateId, 0),
            ev.GetCallHistoriesIncludesItself());
    }
}
