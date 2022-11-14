using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record DeleteClient(Guid ClientId) : ChangeCommandBase<Client>
{
    public DeleteClient() : this(Guid.Empty) { }
    public override Guid GetAggregateId() => ClientId;
    public class Handler : ChangeCommandHandlerBase<Client, DeleteClient>
    {
        protected override async IAsyncEnumerable<IChangedEvent<Client>> ExecCommandAsync(
            Func<AggregateState<Client>> getAggregateStateState,
            DeleteClient command)
        {
            await Task.CompletedTask;
            yield return new ClientDeleted();
        }
    }
}
