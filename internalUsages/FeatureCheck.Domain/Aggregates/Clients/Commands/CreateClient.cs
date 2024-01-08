using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Shared.Exceptions;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
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
    public string GetRootPartitionKey() => IDocument.DefaultRootPartitionKey;

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandlerAsync<Client, CreateClient>
    {
        private readonly IQueryExecutor queryExecutor;

        public Handler(IQueryExecutor queryExecutor) => this.queryExecutor = queryExecutor;

        public async IAsyncEnumerable<IEventPayloadApplicableTo<Client>> HandleCommandAsync(CreateClient command, ICommandContext<Client> context)
        {
            // Check if branch exists
            var branchExistsOutput = await queryExecutor.ExecuteAsync(new BranchExistsQuery.Parameter(command.BranchId));
            if (!branchExistsOutput.Exists)
            {
                throw new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch), (command as ICommandCommon).GetRootPartitionKey());
            }

            // Check no email duplicates
            var emailExistsOutput = await queryExecutor.ExecuteAsync(
                new ClientEmailExistsQuery.Parameter(command.ClientEmail) { RootPartitionKey = (command as ICommandCommon).GetRootPartitionKey() });
            yield return emailExistsOutput.Exists
                ? throw new SekibanEmailAlreadyRegistered()
                : (IEventPayloadApplicableTo<Client>)new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail);
        }
    }
}
