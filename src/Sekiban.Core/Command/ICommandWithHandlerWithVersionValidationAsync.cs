using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithVersionValidationAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerAsync<TAggregatePayload, TCommand>,
    IVersionValidationCommandCommon where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommand<TAggregatePayload>
{
}
