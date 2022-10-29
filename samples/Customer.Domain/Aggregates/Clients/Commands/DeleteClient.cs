using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record DeleteClient(Guid ClientId) : ChangeAggregateCommandBase<Client>
{
    public DeleteClient() : this(Guid.Empty) { }
    public override Guid GetAggregateId()
    {
        return ClientId;
    }
}
public class DeleteClientHandler : ChangeAggregateCommandHandlerBase<Client, DeleteClient>
{
    protected override async IAsyncEnumerable<IChangedEvent<Client>> ExecCommandAsync(Func<AggregateState<Client>> getAggregateStateState, DeleteClient command)
    {
        await Task.CompletedTask;
        yield return new ClientDeleted();
    }
}
