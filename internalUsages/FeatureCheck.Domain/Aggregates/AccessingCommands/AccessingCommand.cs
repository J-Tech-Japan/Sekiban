using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.AccessingCommands;

public record AccessingCommand(Guid CreatedCommandId, Guid AggregateId) : IAggregatePayload<AccessingCommand>
{
    public static AccessingCommand CreateInitialPayload(AccessingCommand? _) => new(Guid.Empty, Guid.Empty);
}
public record CreateAccessingCommand : ICommandWithHandler<AccessingCommand, CreateAccessingCommand>
{
    public static Guid SpecifyAggregateId(CreateAccessingCommand command) => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(
        CreateAccessingCommand command,
        ICommandContext<AccessingCommand> context) =>
        context.AppendEvent(
            new AccessingCommandAggregateCreated(context.GetCommandDocument().Id, context.GetAggregateId()));
}
public record AccessingCommandAggregateCreated(Guid CommandId, Guid AggregateId)
    : IEventPayload<AccessingCommand, AccessingCommandAggregateCreated>
{
    public static AccessingCommand OnEvent(
        AccessingCommand aggregatePayload,
        Event<AccessingCommandAggregateCreated> ev) => aggregatePayload with
    {
        CreatedCommandId = ev.Payload.CommandId, AggregateId = ev.Payload.AggregateId
    };
}
