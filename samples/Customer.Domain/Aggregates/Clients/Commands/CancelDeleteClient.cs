using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record CancelDeleteClient : ChangeAggregateCommandBase<Client>, ICancelDeletedCommand
{
    public Guid ClientId { get; init; }
    [Required]
    public string Reason { get; init; } = string.Empty;
    public override Guid GetAggregateId()
    {
        return ClientId;
    }
}
public class CancelDeleteClientHandler : ChangeAggregateCommandHandlerBase<Client, CancelDeleteClient>
{

    protected override async IAsyncEnumerable<IChangedEvent<Client>> ExecCommandAsync(
        Func<AggregateState<Client>> getAggregateState,
        CancelDeleteClient command)
    {
        await Task.CompletedTask;
        yield return new ClientDeleteCancelled();
    }
}
