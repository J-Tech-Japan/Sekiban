using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Shared.Exceptions;
using ResultBoxes;
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
            if (emailExistsOutput.Exists)
            {
                throw new SekibanEmailAlreadyRegistered();
            }

            yield return new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail);
        }
    }
}
public record CreateClientWithResult(
    [property: Required]
    Guid BranchId,
    [property: Required]
    string ClientName,
    [property: Required]
    string ClientEmail) : ICommandWithStaticHandlerAsync<Client, CreateClientWithResult>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public static async Task<ResultBox<UnitValue>> HandleCommandAsync(CreateClientWithResult command, ICommandContext<Client> context) =>
        await context.GetRequiredService<IQueryExecutor>()
            .Combine(queryExecutor => queryExecutor.ExecuteWithResultAsync(new BranchExistsQuery.Parameter(command.BranchId)))
            .Verify(
                values => values.Value2.Exists
                    ? ExceptionOrNone.None
                    : new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch), (command as ICommandCommon).GetRootPartitionKey()))
            .Remap(values => values.Value1)
            .Conveyor(
                queryExecutor => queryExecutor.ExecuteWithResultAsync(
                    new ClientEmailExistsQuery.Parameter(command.ClientEmail)
                    {
                        RootPartitionKey = (command as ICommandCommon).GetRootPartitionKey()
                    }))
            .Verify(response => response.Exists ? new SekibanEmailAlreadyRegistered() : ExceptionOrNone.None)
            .Conveyor(_ => context.AppendEvent(new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail)));
}
