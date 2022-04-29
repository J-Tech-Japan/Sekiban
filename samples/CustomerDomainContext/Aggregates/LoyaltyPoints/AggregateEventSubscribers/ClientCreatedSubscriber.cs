using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.AggregateEventSubscribers;

public class ClientCreatedSubscriber : AggregateEventSubscriberBase<ClientCreated>
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;

    public ClientCreatedSubscriber(AggregateCommandExecutor aggregateCommandExecutor) =>
        _aggregateCommandExecutor = aggregateCommandExecutor;

    public override async Task SubscribeAggregateEventAsync(ClientCreated ev)
    {
        await _aggregateCommandExecutor
            .ExecCreateCommandAsync<LoyaltyPoint, LoyaltyPointDto, CreateLoyaltyPoint>(
                new CreateLoyaltyPoint(ev.ClientId, 0),
                ev.GetCallHistoriesIncludesItself());
    }
}
