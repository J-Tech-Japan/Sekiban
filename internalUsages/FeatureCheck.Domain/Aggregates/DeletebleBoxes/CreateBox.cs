using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record CreateBox(string Code, string Name) : ICommandWithHandler<Box, CreateBox>
{

    public static Guid SpecifyAggregateId(CreateBox command) => Guid.NewGuid();
    public static ResultBox<EventOrNone<Box>> HandleCommand(CreateBox command, ICommandContext<Box> context) =>
        context.AppendEvent(new BoxCreated(command.Code, command.Name));
}
