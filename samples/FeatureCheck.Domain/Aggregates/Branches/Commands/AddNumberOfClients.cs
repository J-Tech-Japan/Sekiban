using FeatureCheck.Domain.Aggregates.Branches.Events;
using FeatureCheck.Domain.Aggregates.Clients;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public class AddNumberOfClients : ICommand<Branch>
{
    public Guid BranchId { get; init; }
    public Guid ClientId { get; init; }
    public Guid GetAggregateId() => BranchId;
    public class Handler : ICommandHandlerAsync<Branch, AddNumberOfClients>
    {
        private readonly IAggregateLoader aggregateLoader;
        public Handler(IAggregateLoader aggregateLoader) => this.aggregateLoader = aggregateLoader;

        public async IAsyncEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommandAsync(
            Func<AggregateState<Branch>> getAggregateState,
            AddNumberOfClients command)
        {
            var result = await aggregateLoader.AsDefaultStateAsync<Client>(command.ClientId);
            if (result is not null)
            {
                yield return new BranchMemberAdded(command.ClientId);
            }
        }
    }
}
