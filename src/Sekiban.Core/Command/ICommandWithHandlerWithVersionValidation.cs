using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithVersionValidation<TAggregatePayload, in TCommand> :
    ICommandWithHandler<TAggregatePayload, TCommand>,
    IVersionValidationCommandCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
}
