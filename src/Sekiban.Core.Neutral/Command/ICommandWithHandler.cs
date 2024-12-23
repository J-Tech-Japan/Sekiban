using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandler<TAggregatePayload, in TCommand> : ICommandWithHandlerAbove<TAggregatePayload, TCommand>,
    IAggregatePayloadCommon<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>;
