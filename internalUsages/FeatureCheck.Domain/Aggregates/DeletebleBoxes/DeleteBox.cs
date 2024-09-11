using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record DeleteBox(Guid BoxId) : ICommandWithHandlerForExistingAggregate<Box, DeleteBox>
{
    public static ResultBox<UnitValue> HandleCommand(DeleteBox command, ICommandContext<Box> context) =>
        context.AppendEvent(new BoxDeleted());
    public static Guid SpecifyAggregateId(DeleteBox command) => command.BoxId;
}
