using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace FeatureCheck.Domain.EventSubscribers;

public class ClientDeletedSubscriber : IEventSubscriber<ClientDeleted>
{
    private readonly ICommandExecutor commandExecutor;

    public ClientDeletedSubscriber(ICommandExecutor commandExecutor) => this.commandExecutor = commandExecutor;

    public async Task HandleEventAsync(Event<ClientDeleted> ev)
    {
        await commandExecutor.ExecCommandAsync(new DeleteLoyaltyPoint(ev.AggregateId), ev.GetCallHistoriesIncludesItself());
    }
}
