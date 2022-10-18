using Sekiban.Core.Command;
namespace CustomerWithTenantAddonDomainContext.Aggregates.Clients.Commands;

public record DeleteClient(Guid ClientId) : ChangeAggregateCommandBase<Client>
{
    public DeleteClient() : this(Guid.Empty) { }
    public override Guid GetAggregateId()
    {
        return ClientId;
    }
}
public class DeleteClientHandler : ChangeAggregateCommandHandlerBase<Client, DeleteClient>
{
    protected override async Task ExecCommandAsync(Client aggregate, DeleteClient _)
    {
        aggregate.Delete();
        await Task.CompletedTask;
    }
}
