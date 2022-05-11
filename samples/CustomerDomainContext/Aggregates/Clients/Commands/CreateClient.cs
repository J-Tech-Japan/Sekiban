using CustomerDomainContext.Aggregates.Branches;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace CustomerDomainContext.Aggregates.Clients.Commands;

public record CreateClient(Guid BranchId, string ClientName, string ClientEmail) : ICreateAggregateCommand<Client>;
public class CreateClientHandler : CreateAggregateCommandHandlerBase<Client, CreateClient>
{
    private readonly SingleAggregateService _singleAggregateService;

    public CreateClientHandler(SingleAggregateService singleAggregateService) =>
        _singleAggregateService = singleAggregateService;

    protected override async Task<Client> CreateAggregateAsync(CreateClient command)
    {
        // Check if branch exists
        var branchDto = await _singleAggregateService.GetAggregateDtoAsync<Branch, BranchDto>(command.BranchId);
        if (branchDto is null)
        {
            throw new JJAggregateNotExistsException(command.BranchId, nameof(Branch));
        }

        return new Client(command.BranchId, command.ClientName, command.ClientEmail);
    }
}
