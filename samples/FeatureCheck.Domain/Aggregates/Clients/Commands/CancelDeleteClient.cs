using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record CancelDeleteClient : IVersionValidationCommandBase<Clients.Client>, ICancelDeletedCommand
{
    public Guid ClientId { get; init; }
    [Required]
    public string Reason { get; init; } = string.Empty;
    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;
    public class Handler : IVersionValidationCommandHandlerBase<Clients.Client, CancelDeleteClient>
    {
        public async IAsyncEnumerable<IEventPayload<Clients.Client>> HandleCommandAsync(
            Func<AggregateState<Clients.Client>> getAggregateState,
            CancelDeleteClient command)
        {
            await Task.CompletedTask;
            yield return new ClientDeleteCancelled();
        }
    }
}
