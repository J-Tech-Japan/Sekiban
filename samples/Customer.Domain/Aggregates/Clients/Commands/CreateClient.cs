﻿using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.QueryFilters;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.QueryFilters;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleAggregate;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record CreateClient : ICreateAggregateCommand<Client>
{
    public CreateClient() : this(Guid.Empty, string.Empty, string.Empty) { }
    public CreateClient(Guid branchId, string clientName, string clientEmail)
    {
        BranchId = branchId;
        ClientName = clientName;
        ClientEmail = clientEmail;
    }
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
    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
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

    protected override async IAsyncEnumerable<IApplicableEvent<Client>> ExecCreateCommandAsync(AggregateState<Client> aggregateState, CreateClient command)
    {
        // Check if branch exists
        var branchExists
            = await _queryFilterService
                .GetAggregateQueryFilterAsync<Branch, BranchExistsQueryFilter, BranchExistsQueryFilter.QueryParameter, bool>(
                    new BranchExistsQueryFilter.QueryParameter(command.BranchId));
        if (!branchExists)
        {
            throw new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch));
        }

        // Check no email duplicates
        var emailExists
            = await _queryFilterService
                .GetAggregateQueryFilterAsync<Client, ClientEmailExistsQueryFilter, ClientEmailExistsQueryFilter.QueryParameter,
                    bool>(new ClientEmailExistsQueryFilter.QueryParameter(command.ClientEmail));
        if (emailExists)
        {
            throw new SekibanEmailAlreadyRegistered();
        }

        yield return new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail);
    }
}