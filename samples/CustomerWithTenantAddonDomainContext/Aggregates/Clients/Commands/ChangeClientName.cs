﻿namespace CustomerWithTenantAddonDomainContext.Aggregates.Clients.Commands;

public record ChangeClientName(Guid ClientId, string ClientName) : ChangeAggregateCommandBase<Client>
{
    public ChangeClientName() : this(Guid.Empty, string.Empty) { }
    public override Guid GetAggregateId() =>
        ClientId;
}
public class ChangeClientNameHandler : ChangeAggregateCommandHandlerBase<Client, ChangeClientName>
{
    protected override async Task ExecCommandAsync(Client aggregate, ChangeClientName command)
    {
        aggregate.ChangeClientName(command.ClientName);
        await Task.CompletedTask;
    }
}