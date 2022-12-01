using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;

namespace Customer.Domain.Aggregates.Clients.Commands;

public record ChangeClientName(Guid ClientId, string ClientName) : IVersionValidationCommand<Client>,
    ICleanupNecessaryCommand<ChangeClientName>
{
    public ChangeClientName() : this(Guid.Empty, string.Empty)
    {
    }

    public ChangeClientName CleanupCommandIfNeeded(ChangeClientName command)
    {
        return command with { ClientName = "stripped for security" };
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId()
    {
        return ClientId;
    }

    public class Handler : IVersionValidationCommandHandler<Client, ChangeClientName>
    {
        public async IAsyncEnumerable<IEventPayload<Client>> HandleCommandAsync(
            Func<AggregateState<Client>> getAggregateState,
            ChangeClientName command)
        {
            await Task.CompletedTask;
            yield return new ClientNameChanged(command.ClientName);
        }
    }
}
