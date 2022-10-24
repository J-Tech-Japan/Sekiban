using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.QueryFilters;
using Customer.Domain.Aggregates.Clients.QueryFilters;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Command;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleAggregate;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Clients.Commands;

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
    private readonly IQueryFilterService _queryFilterService;
    private readonly ISingleAggregateService _singleAggregateService;
    public CreateClientHandler(ISingleAggregateService singleAggregateService, IQueryFilterService queryFilterService)
    {
        _singleAggregateService = singleAggregateService;
        _queryFilterService = queryFilterService;
    }

    public override Guid GenerateAggregateId(CreateClient command)
    {
        return Guid.NewGuid();
    }
    protected override async Task ExecCreateCommandAsync(Client aggregate, CreateClient command)
    {
        // Check if branch exists
        var branchExists
            = await _queryFilterService
                .GetAggregateQueryFilterAsync<Branch, BranchPayload, BranchExistsQueryFilter, BranchExistsQueryFilter.QueryParameter, bool>(
                    new BranchExistsQueryFilter.QueryParameter(command.BranchId));
        if (!branchExists)
        {
            throw new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch));
        }

        // Check no email duplicates
        var emailExists
            = await _queryFilterService
                .GetAggregateQueryFilterAsync<Client, ClientPayload, ClientEmailExistsQueryFilter, ClientEmailExistsQueryFilter.QueryParameter,
                    bool>(new ClientEmailExistsQueryFilter.QueryParameter(command.ClientEmail));
        if (emailExists)
        {
            throw new SekibanEmailAlreadyRegistered();
        }

        aggregate.CreateClient(command.BranchId, command.ClientName, command.ClientEmail);
    }
}
