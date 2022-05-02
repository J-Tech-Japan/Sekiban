namespace CustomerDomainContext.Aggregates.Clients.Commands;

public record DeleteClient(
    Guid ClientId
) : ChangeAggregateCommandBase<Client>(
    ClientId
);
public class DeleteClientHandler : ChangeAggregateCommandHandlerBase<Client, DeleteClient>
{
    protected override async Task ExecCommandAsync(Client aggregate, DeleteClient _)
    {
        aggregate.Delete();
        await Task.CompletedTask;
    }
}
