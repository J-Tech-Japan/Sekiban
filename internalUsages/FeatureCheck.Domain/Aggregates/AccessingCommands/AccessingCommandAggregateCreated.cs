using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.AccessingCommands;

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
