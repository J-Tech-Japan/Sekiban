using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
namespace CustomerDomainContext.AggregateEventSubscribers;

public class ClientCreatedSubscriber : AggregateEventSubscriberBase<ClientCreated>
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;

    public ClientCreatedSubscriber(IAggregateCommandExecutor aggregateCommandExecutor) =>
        _aggregateCommandExecutor = aggregateCommandExecutor;

    public override async Task SubscribeAggregateEventAsync(ClientCreated ev)
    {
        await _aggregateCommandExecutor.ExecCreateCommandAsync<LoyaltyPoint, LoyaltyPointDto, CreateLoyaltyPoint>(
            new CreateLoyaltyPoint(ev.ClientId, 0),
            ev.GetCallHistoriesIncludesItself());
    }
}
