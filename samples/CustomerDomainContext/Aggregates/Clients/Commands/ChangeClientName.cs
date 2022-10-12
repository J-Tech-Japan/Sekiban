namespace CustomerDomainContext.Aggregates.Clients.Commands;

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
    protected override async Task ExecCommandAsync(Client aggregate, ChangeClientName command)
    {
        aggregate.ChangeClientName(command.ClientName);
        await Task.CompletedTask;
    }
    public override ChangeClientName CleanupCommandIfNeeded(ChangeClientName command)
    {
        return command with { ClientName = "stripped for security" };
    }
}
