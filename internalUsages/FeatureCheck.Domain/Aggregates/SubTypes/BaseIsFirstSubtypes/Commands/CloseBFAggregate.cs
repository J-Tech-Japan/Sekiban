using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record CloseBFAggregate(Guid BfAggregateId) : ICommand<ActiveBFAggregate>
{
    public class Handler : ICommandHandler<ActiveBFAggregate, CloseBFAggregate>
    {
        public IEnumerable<IEventPayloadApplicableTo<ActiveBFAggregate>> HandleCommand(
            CloseBFAggregate command,
            ICommandContext<ActiveBFAggregate> context)
        {
            yield return new BFAggregateClosed();
        }
        public Guid SpecifyAggregateId(CloseBFAggregate command) => command.BfAggregateId;
    }
}
