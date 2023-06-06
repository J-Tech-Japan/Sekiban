using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record CreateClient : ICommand<Client>
{

    [Required]
    public Guid BranchId { get; init; }

    [Required]
    public string ClientName { get; init; }

    [Required]
    public string ClientEmail { get; init; }
    public CreateClient() : this(Guid.Empty, string.Empty, string.Empty)
    {
    }

    public CreateClient(Guid branchId, string clientName, string clientEmail)
    {
        BranchId = branchId;
        ClientName = clientName;
        ClientEmail = clientEmail;
    }

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<Client, CreateClient>
    {
        private readonly IAggregateLoader aggregateLoader;
        private readonly IQueryExecutor queryExecutor;

        public Handler(IAggregateLoader aggregateLoader, IQueryExecutor queryExecutor)
        {
            this.aggregateLoader = aggregateLoader;
            this.queryExecutor = queryExecutor;
        }

        public async IAsyncEnumerable<IEventPayloadApplicableTo<Client>> HandleCommandAsync(
            Func<AggregateState<Client>> getAggregateStateState,
            CreateClient command)
        {
            // Check if branch exists
            var branchExistsOutput = await queryExecutor.ExecuteAsync(new BranchExistsQuery.Parameter(command.BranchId));
            if (!branchExistsOutput.Exists)
            {
                throw new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch));
            }

            // Check no email duplicates
            var emailExistsOutput = await queryExecutor.ExecuteAsync(new ClientEmailExistsQuery.Parameter(command.ClientEmail));
            if (emailExistsOutput.Exists)
            {
                throw new SekibanEmailAlreadyRegistered();
            }

            yield return new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail);
        }
    }
}
