using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.AccessingCommands;

public record AccessingCommand(Guid CreatedCommandId, Guid AggregateId) : IAggregatePayload<AccessingCommand>
{
    public static AccessingCommand CreateInitialPayload(AccessingCommand? _) => new(Guid.Empty, Guid.Empty);
}
