using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithVersionValidationForExistingAggregate<TAggregatePayload, in TCommand> : ICommandWithHandler<TAggregatePayload, TCommand>,
    IVersionValidationCommandCommon,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
}
