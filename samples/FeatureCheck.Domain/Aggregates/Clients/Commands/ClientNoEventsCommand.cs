using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record ClientNoEventsCommand : ICommand<Client>
{
    public Guid ClientId { get; init; }
    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandler<Client, ClientNoEventsCommand>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<Client>> HandleCommandAsync(
            Func<AggregateState<Client>> getAggregateState,
            ClientNoEventsCommand command)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
