using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record ReopenBFAggregate(Guid BfAggregateId) : ICommand<ClosedBFAggregate>
{
    public Guid GetAggregateId() => BfAggregateId;

    public class Handler : ICommandHandler<ClosedBFAggregate, ReopenBFAggregate>
    {
        public IEnumerable<IEventPayloadApplicableTo<ClosedBFAggregate>> HandleCommand(
            Func<AggregateState<ClosedBFAggregate>> getAggregateState,
            ReopenBFAggregate command)
        {
            yield return new BFAggregateReopened();
        }
    }
}
