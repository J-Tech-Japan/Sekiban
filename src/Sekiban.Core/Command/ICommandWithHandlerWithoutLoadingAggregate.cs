using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandWithHandlerWithoutLoadingAggregate<TAggregatePayload, in TCommand> : ICommandWithHandler<TAggregatePayload, TCommand>,
    ICommandWithoutLoadingAggregateCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
}
