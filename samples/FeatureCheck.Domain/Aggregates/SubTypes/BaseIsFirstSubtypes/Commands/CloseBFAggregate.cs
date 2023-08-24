using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record CloseBFAggregate(Guid BfAggregateId) : ICommand<ActiveBFAggregate>
{
    public Guid GetAggregateId() => BfAggregateId;

    public class Handler : ICommandHandler<ActiveBFAggregate, CloseBFAggregate>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ActiveBFAggregate>> HandleCommandAsync(
            Func<AggregateState<ActiveBFAggregate>> getAggregateState,
            CloseBFAggregate command)
        {
            await Task.CompletedTask;
            yield return new BFAggregateClosed();
        }
    }
}
