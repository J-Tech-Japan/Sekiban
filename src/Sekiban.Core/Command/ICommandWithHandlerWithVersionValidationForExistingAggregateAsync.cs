using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithVersionValidationForExistingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerAsync<TAggregatePayload, TCommand>, IVersionValidationCommandCommon,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
}
