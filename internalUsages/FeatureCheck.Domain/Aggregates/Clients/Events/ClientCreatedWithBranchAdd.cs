using FeatureCheck.Domain.Aggregates.Branches.Commands;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace FeatureCheck.Domain.Aggregates.Clients.Events;

public record ClientCreatedWithBranchAdd(Guid BranchId, string ClientName, string ClientEmail)
    : IEventPayload<Client, ClientCreatedWithBranchAdd>
{
    public static Client OnEvent(Client aggregatePayload, Event<ClientCreatedWithBranchAdd> ev) => new(ev.Payload.BranchId, ev.Payload.ClientName, ev.Payload.ClientEmail);

    public class BranchSubscriber : IEventSubscriber<ClientCreatedWithBranchAdd, BranchSubscriber>
    {
        private readonly ICommandExecutor _commandExecutor;

        public BranchSubscriber(ICommandExecutor commandExecutor) => _commandExecutor = commandExecutor;

        public async Task HandleEventAsync(Event<ClientCreatedWithBranchAdd> ev)
        {
            await Task.Delay(1000);
            await _commandExecutor.ExecCommandAsync(new AddNumberOfClients
            {
                BranchId = ev.Payload.BranchId, ClientId = ev.AggregateId
            });
        }
    }
}
