using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record CancelDeleteClient : ChangeCommandBase<Client>, ICancelDeletedCommand
{
    public Guid ClientId { get; init; }
    [Required]
    public string Reason { get; init; } = string.Empty;
    public override Guid GetAggregateId() => ClientId;
    public class Handler : ChangeCommandHandlerBase<Client, CancelDeleteClient>
    {
        protected override async IAsyncEnumerable<IApplicableEvent<Client>> ExecCommandAsync(
            Func<AggregateState<Client>> getAggregateState,
            CancelDeleteClient command)
        {
            await Task.CompletedTask;
            yield return new ClientDeleteCancelled();
        }
    }
}
