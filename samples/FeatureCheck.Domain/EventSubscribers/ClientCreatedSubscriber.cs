using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace FeatureCheck.Domain.EventSubscribers;

public class ClientCreatedSubscriber : IEventSubscriber<ClientCreated>
{
    private readonly ICommandExecutor commandExecutor;

    public ClientCreatedSubscriber(ICommandExecutor commandExecutor)
    {
        this.commandExecutor = commandExecutor;
    }

    public async Task HandleEventAsync(Event<ClientCreated> ev)
    {
        await commandExecutor.ExecCommandAsync(new CreateLoyaltyPoint(ev.AggregateId, 0), ev.GetCallHistoriesIncludesItself());
    }
}
