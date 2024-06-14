using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithoutLoadingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerCommon<TAggregatePayload, TCommand>,
    ICommandWithoutLoadingAggregateCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
    public static abstract Task<ResultBox<UnitValue>> HandleCommandAsync(TCommand command, ICommandContextWithoutGetState<TAggregatePayload> context);
}