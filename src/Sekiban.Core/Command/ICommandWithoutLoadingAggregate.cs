using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for define a command that does not need aggregate state to publish event.
///     This command type is used for commands that does not get affected by the current state of the aggregate.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface ICommandWithoutLoadingAggregate<TAggregatePayload> : ICommand<TAggregatePayload>, ICommandWithoutLoadingAggregateCommon
    where TAggregatePayload : IAggregatePayloadCommon;
