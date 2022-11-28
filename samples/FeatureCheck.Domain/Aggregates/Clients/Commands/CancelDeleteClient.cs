using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record CancelDeleteClient : ChangeCommandBase<Clients.Client>, ICancelDeletedCommand
{
    public Guid ClientId { get; init; }
    [Required]
    public string Reason { get; init; } = string.Empty;
    public override Guid GetAggregateId() => ClientId;
    public class Handler : ChangeCommandHandlerBase<Clients.Client, CancelDeleteClient>
    {
        protected override async IAsyncEnumerable<IApplicableEvent<Clients.Client>> ExecCommandAsync(
            Func<AggregateState<Clients.Client>> getAggregateState,
            CancelDeleteClient command)
        {
            await Task.CompletedTask;
            yield return new ClientDeleteCancelled();
        }
    }
}
