using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Common;
using FeatureCheck.Domain.Shared.Exceptions;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record CreateClientWithBranchSubscriber : ICommand<Client>
{
    public CreateClientWithBranchSubscriber() : this(Guid.Empty, string.Empty, string.Empty)
    {
    }

    public CreateClientWithBranchSubscriber(Guid branchId, string clientName, string clientEmail)
    {
        BranchId = branchId;
        ClientName = clientName;
        ClientEmail = clientEmail;
    }

    [Required] public Guid BranchId { get; init; }

    [Required] public string ClientName { get; init; }

    [Required] public string ClientEmail { get; init; }

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandlerAsync<Client, CreateClientWithBranchSubscriber>
    {
        private readonly DependencyInjectionSampleService dependencyInjectionSampleService;
        private readonly IQueryExecutor queryExecutor;

        public Handler(IQueryExecutor queryExecutor, DependencyInjectionSampleService dependencyInjectionSampleService)
        {
            this.queryExecutor = queryExecutor;
            this.dependencyInjectionSampleService = dependencyInjectionSampleService;
        }

        public async IAsyncEnumerable<IEventPayloadApplicableTo<Client>> HandleCommandAsync(
            CreateClientWithBranchSubscriber command,
            ICommandContext<Client> context)
        {
            // Check if branch exists
            var branchExistsOutput =
                await queryExecutor.ExecuteAsync(new BranchExistsQuery.Parameter(command.BranchId));
            if (!branchExistsOutput.Exists)
                throw new SekibanAggregateNotExistsException(command.BranchId, nameof(Branch),
                    (command as ICommandCommon).GetRootPartitionKey());

            // Check no email duplicates
            var emailExistsOutput =
                await queryExecutor.ExecuteAsync(new ClientEmailExistsQuery.Parameter(command.ClientEmail));
            if (emailExistsOutput.Exists)
                throw new SekibanEmailAlreadyRegistered();
            var emailExistsOutput2 = await dependencyInjectionSampleService.ExistsClientEmail(command.ClientEmail);
            if (emailExistsOutput2)
                throw new SekibanEmailAlreadyRegistered();

            yield return new ClientCreatedWithBranchAdd(command.BranchId, command.ClientName, command.ClientEmail);
        }
    }
}
