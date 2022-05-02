namespace CustomerDomainContext.Aggregates.Clients.Commands;

public record ChangeClientName(
    Guid ClientId,
    string ClientName
) : ChangeAggregateCommandBase<Client>(
    ClientId
);
public class ChangeClientNameHandler : ChangeAggregateCommandHandlerBase<Client, ChangeClientName>
{
    protected override async Task ExecCommandAsync(Client aggregate, ChangeClientName command)
    {
        aggregate.ChangeClientName(command.ClientName);
        await Task.CompletedTask;
    }
}
