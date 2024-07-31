using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;
using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;

public record CreateInheritInSubtypesType(int FirstProperty)
    : ICommandWithHandler<FirstStage, CreateInheritInSubtypesType>
{
    public Guid GetAggregateId() => Guid.NewGuid();

    public static ResultBox<UnitValue> HandleCommand(
        CreateInheritInSubtypesType command,
        ICommandContext<FirstStage> context) =>
        context.AppendEvent(new InheritInSubTypesCreated(command.FirstProperty));
}
