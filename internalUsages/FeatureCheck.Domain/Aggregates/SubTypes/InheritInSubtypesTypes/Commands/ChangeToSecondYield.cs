using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;

public record ChangeToSecondYield(Guid AggregateId, int SecondProperty) : ICommand<FirstStage>
{
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<FirstStage, ChangeToSecondYield>
    {
        public IEnumerable<IEventPayloadApplicableTo<FirstStage>> HandleCommand(
            ChangeToSecondYield command,
            ICommandContext<FirstStage> context)
        {
            yield return new InheritInSubtypesMovedToSecond(command.SecondProperty);
        }
    }
}
