using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Queries;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Queries;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using System.ComponentModel.DataAnnotations;
namespace Customer.Domain.Aggregates.Clients.Commands;

public record CreateClient : ICreateCommand<Client>
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
    public Guid GetAggregateId() => Guid.NewGuid();
    public class Handler : CreateCommandHandlerBase<Client, CreateClient>
    {
        private readonly IAggregateLoader aggregateLoader;
        private readonly IQueryExecutor queryExecutor;
        public Handler(IAggregateLoader aggregateLoader, IQueryExecutor queryExecutor)
        {
            this.aggregateLoader = aggregateLoader;
            this.queryExecutor = queryExecutor;
        }

        protected override async IAsyncEnumerable<IApplicableEvent<Client>> ExecCreateCommandAsync(
            Func<AggregateState<Client>> getAggregateStateState,
            CreateClient command)
        {
            // Check if branch exists
            var branchExists
                = await queryExecutor
                    .ForAggregateQueryAsync<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                        new BranchExistsQuery.QueryParameter(command.BranchId));
            if (!branchExists)
            {
                throw new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch));
            }

            // Check no email duplicates
            var emailExists
                = await queryExecutor
                    .ForAggregateQueryAsync<Client, ClientEmailExistsQuery, ClientEmailExistsQuery.QueryParameter,
                        bool>(new ClientEmailExistsQuery.QueryParameter(command.ClientEmail));
            if (emailExists)
            {
                throw new SekibanEmailAlreadyRegistered();
            }

            yield return new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail);
        }
    }
}
