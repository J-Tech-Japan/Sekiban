using FeatureCheck.Domain.Aggregates.Branches.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientCreated(Guid BranchId, string ClientName, string ClientEmail) : IEventPayload<Client, ClientCreated>
{
    public static Client OnEvent(Client aggregatePayload, Event<ClientCreated> ev) =>
        new(ev.Payload.BranchId, ev.Payload.ClientName, ev.Payload.ClientEmail);

    public class BranchSubscriber : IEventSubscriber<ClientCreated, BranchSubscriber>
    {
        private readonly ICommandExecutor _commandExecutor;
        public BranchSubscriber(ICommandExecutor commandExecutor) => _commandExecutor = commandExecutor;
        public async Task HandleEventAsync(Event<ClientCreated> ev)
        {
            await Task.Delay(1000);
            await _commandExecutor.ExecCommandAsync(new AddNumberOfClients { BranchId = ev.Payload.BranchId, ClientId = ev.AggregateId });
        }
    }
}
