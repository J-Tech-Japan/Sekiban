using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record ChangeClientName(Guid ClientId, string ClientName) : IVersionValidationCommand<Clients.Client>, ICleanupNecessaryCommand<ChangeClientName>
{


    public ChangeClientName() : this(Guid.Empty, string.Empty) { }
    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;
    public class Handler : IVersionValidationCommandHandlerBase<Clients.Client, ChangeClientName>
    {
        public async IAsyncEnumerable<IEventPayload<Clients.Client>> HandleCommandAsync(
            Func<AggregateState<Clients.Client>> getAggregateState,
            ChangeClientName command)
        {
            await Task.CompletedTask;
            yield return new ClientNameChanged(command.ClientName);
        }
    }
    public ChangeClientName CleanupCommandIfNeeded(ChangeClientName command) => command with { ClientName = "stripped for security" };
}
