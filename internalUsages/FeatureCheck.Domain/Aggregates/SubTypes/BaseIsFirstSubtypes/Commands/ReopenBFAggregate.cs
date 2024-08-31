using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record ReopenBFAggregate(Guid BfAggregateId) : ICommand<ClosedBFAggregate>
{
    public Guid GetAggregateId() => BfAggregateId;

    public class Handler : ICommandHandler<ClosedBFAggregate, ReopenBFAggregate>
    {
        public IEnumerable<IEventPayloadApplicableTo<ClosedBFAggregate>> HandleCommand(
            ReopenBFAggregate command,
            ICommandContext<ClosedBFAggregate> context)
        {
            yield return new BFAggregateReopened();
        }
        public Guid SpecifyAggregateId(ReopenBFAggregate command) => command.BfAggregateId;
    }
}
