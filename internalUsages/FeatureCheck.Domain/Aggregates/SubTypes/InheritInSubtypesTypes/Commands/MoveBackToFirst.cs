using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;
using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;

public record MoveBackToFirst(Guid AggregateId) : ICommandWithHandler<SecondStage, MoveBackToFirst>
{
    public Guid GetAggregateId() => AggregateId;
    public static ResultBox<UnitValue> HandleCommand(MoveBackToFirst command, ICommandContext<SecondStage> context) =>
        context.AppendEvent(new InheritInSubtypesMovedSecondToFirst());
}
