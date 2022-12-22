using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record CancelDeleteClient : IVersionValidationCommand<Client>, ICancelDeletedCommand
{
    public Guid ClientId { get; init; }

    [Required] public string Reason { get; init; } = string.Empty;

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId()
    {
        return ClientId;
    }

    public class Handler : IVersionValidationCommandHandler<Client, CancelDeleteClient>
    {
        public async IAsyncEnumerable<IEventPayload<Client>> HandleCommandAsync(
            Func<AggregateState<Client>> getAggregateState,
            CancelDeleteClient command)
        {
            await Task.CompletedTask;
            yield return new ClientDeleteCancelled();
        }
    }
}
