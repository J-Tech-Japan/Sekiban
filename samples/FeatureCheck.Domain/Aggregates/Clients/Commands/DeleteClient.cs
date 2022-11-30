using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record DeleteClient(Guid ClientId) : IVersionValidationCommandBase<Clients.Client>
{
    public DeleteClient() : this(Guid.Empty) { }
    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;
    public class Handler : IVersionValidationCommandHandlerBase<Clients.Client, DeleteClient>
    {
        public async IAsyncEnumerable<IApplicableEvent<Clients.Client>> HandleCommandAsync(
            Func<AggregateState<Clients.Client>> getAggregateStateState,
            DeleteClient command)
        {
            await Task.CompletedTask;
            yield return new ClientDeleted();
        }
    }
}
