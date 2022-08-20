using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
namespace CustomerDomainContext.AggregateEventSubscribers
{
    public class ClientCreatedSubscriber : AggregateEventSubscriberBase<ClientCreated>
    {
        private readonly IAggregateCommandExecutor _aggregateCommandExecutor;

        public ClientCreatedSubscriber(IAggregateCommandExecutor aggregateCommandExecutor) =>
            _aggregateCommandExecutor = aggregateCommandExecutor;

        public override async Task SubscribeAggregateEventAsync(AggregateEvent<ClientCreated> ev)
        {
            await _aggregateCommandExecutor.ExecCreateCommandAsync<LoyaltyPoint, LoyaltyPointContents, CreateLoyaltyPoint>(
                new CreateLoyaltyPoint(ev.AggregateId, 0),
                ev.GetCallHistoriesIncludesItself());
        }
    }
}
