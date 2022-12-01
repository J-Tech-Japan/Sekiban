using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.Clients.Commands;

public record DeleteClient(Guid ClientId) : IVersionValidationCommand<Client>
{
    public DeleteClient() : this(Guid.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId()
    {
        return ClientId;
    }

    public class Handler : IVersionValidationCommandHandler<Client, DeleteClient>
    {
        public async IAsyncEnumerable<IEventPayload<Client>> HandleCommandAsync(
            Func<AggregateState<Client>> getAggregateStateState,
            DeleteClient command)
        {
            await Task.CompletedTask;
            yield return new ClientDeleted();
        }
    }
}
