using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.AccessingCommands;

public record CreateAccessingCommand : ICommandWithHandler<AccessingCommand, CreateAccessingCommand>
{
    public static Guid SpecifyAggregateId(CreateAccessingCommand command) => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(
        CreateAccessingCommand command,
        ICommandContext<AccessingCommand> context) =>
        context.AppendEvent(
            new AccessingCommandAggregateCreated(context.GetCommandDocument().Id, context.GetAggregateId()));
}
