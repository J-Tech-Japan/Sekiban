﻿using Customer.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record ChangeClientName(Guid ClientId, string ClientName) : ChangeAggregateCommandBase<Client>
{
    public ChangeClientName() : this(Guid.Empty, string.Empty) { }
    public override Guid GetAggregateId()
    {
        return ClientId;
    }
}
public class ChangeClientNameHandler : ChangeAggregateCommandHandlerBase<Client, ChangeClientName>
{
    public override ChangeClientName CleanupCommandIfNeeded(ChangeClientName command)
    {
        return command with { ClientName = "stripped for security" };
    }
    protected override async IAsyncEnumerable<IChangedEvent<Client>> ExecCommandAsync(AggregateState<Client> aggregateState, ChangeClientName command)
    {
        await Task.CompletedTask;
        yield return new ClientNameChanged(command.ClientName);
    }
}