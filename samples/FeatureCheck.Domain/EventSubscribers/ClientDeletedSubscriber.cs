using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;

namespace Customer.Domain.EventSubscribers;

public class ClientDeletedSubscriber : IEventSubscriber<ClientDeleted>
{
    private readonly ICommandExecutor commandExecutor;

    public ClientDeletedSubscriber(ICommandExecutor commandExecutor)
    {
        this.commandExecutor = commandExecutor;
    }

    public async Task HandleEventAsync(Event<ClientDeleted> ev)
    {
        await commandExecutor.ExecCommandAsync(
            new DeleteLoyaltyPoint(ev.AggregateId),
            ev.GetCallHistoriesIncludesItself());
    }
}
