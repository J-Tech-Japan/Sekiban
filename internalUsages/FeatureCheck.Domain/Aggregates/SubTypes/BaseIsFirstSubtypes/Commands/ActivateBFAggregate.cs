using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record ActivateBFAggregate(Guid BfAggregateId) : ICommand<BaseFirstAggregate>
{
    public Guid GetAggregateId() => BfAggregateId;

    public class Handler : ICommandHandler<BaseFirstAggregate, ActivateBFAggregate>
    {
        public IEnumerable<IEventPayloadApplicableTo<BaseFirstAggregate>> HandleCommand(
            ActivateBFAggregate command,
            ICommandContext<BaseFirstAggregate> context)
        {
            yield return new BFAggregateActivated();
        }
    }
}
