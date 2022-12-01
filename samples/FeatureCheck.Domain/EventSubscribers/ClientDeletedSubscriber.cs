using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;

namespace Customer.Domain.EventSubscribers;

public class ClientDeletedSubscriber : EventSubscriberBase<ClientDeleted>
{
    private readonly ICommandExecutor commandExecutor;

    public ClientDeletedSubscriber(ICommandExecutor commandExecutor)
    {
        this.commandExecutor = commandExecutor;
    }

    public override async Task SubscribeEventAsync(Event<ClientDeleted> ev)
    {
        await commandExecutor.ExecCommandAsync<LoyaltyPoint, DeleteLoyaltyPoint>(
            new DeleteLoyaltyPoint(ev.AggregateId),
            ev.GetCallHistoriesIncludesItself());
    }
}
