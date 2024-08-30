using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;
using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;

public record ChangeToSecond(Guid AggregateId, int SecondProperty) : ICommandWithHandler<FirstStage, ChangeToSecond>
{
    public static ResultBox<UnitValue> HandleCommand(ChangeToSecond command, ICommandContext<FirstStage> context) =>
        context.AppendEvent(new InheritInSubtypesMovedToSecond(command.SecondProperty));
    public static Guid SpecifyAggregateId(ChangeToSecond command) => command.AggregateId;
}
