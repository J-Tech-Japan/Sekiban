using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, in TCommand> : ICommandWithHandlerAsync<TAggregatePayload, TCommand>,
    ICommandWithoutLoadingAggregateCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
}
