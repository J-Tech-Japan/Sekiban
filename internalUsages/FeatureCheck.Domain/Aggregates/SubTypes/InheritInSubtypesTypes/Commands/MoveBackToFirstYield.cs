using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;

public record MoveBackToFirstYield(Guid AggregateId) : ICommand<SecondStage>
{
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<SecondStage, MoveBackToFirstYield>
    {
        public IEnumerable<IEventPayloadApplicableTo<SecondStage>> HandleCommand(
            MoveBackToFirstYield command,
            ICommandContext<SecondStage> context)
        {
            yield return new InheritInSubtypesMovedSecondToFirst();
        }
    }
}
