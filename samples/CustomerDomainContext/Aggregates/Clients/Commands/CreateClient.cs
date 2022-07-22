using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Shared.Exceptions;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace CustomerDomainContext.Aggregates.Clients.Commands;

public record CreateClient(Guid BranchId, string ClientName, string ClientEmail) : ICreateAggregateCommand<Client>;
public class CreateClientHandler : CreateAggregateCommandHandlerBase<Client, CreateClient>
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ISingleAggregateService _singleAggregateService;
    public CreateClientHandler(ISingleAggregateService singleAggregateService, IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _singleAggregateService = singleAggregateService;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }

    protected override async Task<Client> CreateAggregateAsync(CreateClient command)
    {
        // Check if branch exists
        var branchDto = await _singleAggregateService.GetAggregateDtoAsync<Branch, BranchContents>(command.BranchId);
        if (branchDto is null)
        {
            throw new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch));
        }

        // Check no email duplicates
        var list = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
        if (list.Any(a => a.Contents.ClientEmail == command.ClientEmail))
        {
            throw new SekibanEmailAlreadyRegistered();
        }

        return new Client(command.BranchId, command.ClientName, command.ClientEmail);
    }
}
