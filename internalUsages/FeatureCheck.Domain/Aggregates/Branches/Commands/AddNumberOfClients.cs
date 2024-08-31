using FeatureCheck.Domain.Aggregates.Branches.Events;
using FeatureCheck.Domain.Aggregates.Clients;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public class AddNumberOfClients : ICommandForExistingAggregate<Branch>
{
    public Guid BranchId { get; init; }
    public Guid ClientId { get; init; }

    public Guid GetAggregateId() => BranchId;

    public class Handler : ICommandHandlerAsync<Branch, AddNumberOfClients>
    {
        private readonly IAggregateLoader aggregateLoader;

        public Handler(IAggregateLoader aggregateLoader) => this.aggregateLoader = aggregateLoader;

        public async IAsyncEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommandAsync(
            AddNumberOfClients command,
            ICommandContext<Branch> context)
        {
            var result = await aggregateLoader.AsDefaultStateAsync<Client>(command.ClientId);
            if (result is not null) yield return new BranchMemberAdded(command.ClientId);
        }
        public Guid SpecifyAggregateId(AddNumberOfClients command) => command.BranchId;
    }
}
