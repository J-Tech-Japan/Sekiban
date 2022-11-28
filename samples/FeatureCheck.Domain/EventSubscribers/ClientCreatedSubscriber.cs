using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;
namespace Customer.Domain.EventSubscribers;

public class ClientCreatedSubscriber : EventSubscriberBase<ClientCreated>
{
    private readonly ICommandExecutor commandExecutor;

    public ClientCreatedSubscriber(ICommandExecutor commandExecutor) => this.commandExecutor = commandExecutor;

    public override async Task SubscribeEventAsync(Event<ClientCreated> ev)
    {
        await commandExecutor.ExecCommandAsync<LoyaltyPoint, Aggregates.LoyaltyPoints.Commands.LoyaltyPoint>(
            new Aggregates.LoyaltyPoints.Commands.LoyaltyPoint(ev.AggregateId, 0),
            ev.GetCallHistoriesIncludesItself());
    }
}
