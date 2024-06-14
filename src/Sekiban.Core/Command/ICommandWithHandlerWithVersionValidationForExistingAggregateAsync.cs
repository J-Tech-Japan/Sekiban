using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerWithVersionValidationForExistingAggregateAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerCommon<TAggregatePayload, TCommand>, IVersionValidationCommandCommon,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
    public static abstract Task<ResultBox<UnitValue>> HandleCommandAsync(TCommand command, ICommandContext<TAggregatePayload> context);
}