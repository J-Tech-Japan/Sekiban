using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Shared.Exceptions;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using System.ComponentModel.DataAnnotations;
namespace CustomerDomainContext.Aggregates.Clients.Commands;

public record CreateClient : ICreateAggregateCommand<Client>
{
    [Required]
    public Guid BranchId
    {
        get;
        init;
    }
    [Required]
    public string ClientName
    {
        get;
        init;
    }
    [Required]
    public string ClientEmail
    {
        get;
        init;
    }
    public CreateClient() : this(Guid.Empty, string.Empty, string.Empty) { }
    public CreateClient(Guid branchId, string clientName, string clientEmail)
    {
        BranchId = branchId;
        ClientName = clientName;
        ClientEmail = clientEmail;
    }
}
public class CreateClientHandler : CreateAggregateCommandHandlerBase<Client, CreateClient>
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ISingleAggregateService _singleAggregateService;
    public CreateClientHandler(ISingleAggregateService singleAggregateService, IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _singleAggregateService = singleAggregateService;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }

    public override Guid GenerateAggregateId(CreateClient command) =>
        Guid.NewGuid();
    protected override async Task ExecCreateCommandAsync(Client aggregate, CreateClient command)
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

        aggregate.CreateClient(command.BranchId, command.ClientName, command.ClientEmail);
    }
}
