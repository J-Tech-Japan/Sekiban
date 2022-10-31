using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;
namespace Customer.Domain.AggregateEventSubscribers;

public class ClientDeletedSubscriber : AggregateEventSubscriberBase<ClientDeleted>
{
    private readonly ICommandExecutor commandExecutor;

    public ClientDeletedSubscriber(ICommandExecutor commandExecutor) => this.commandExecutor = commandExecutor;

    public override async Task SubscribeAggregateEventAsync(Event<ClientDeleted> ev)
    {
        await commandExecutor.ExecChangeCommandAsync<LoyaltyPoint, DeleteLoyaltyPoint>(
            new DeleteLoyaltyPoint(ev.AggregateId),
            ev.GetCallHistoriesIncludesItself());
    }
}
