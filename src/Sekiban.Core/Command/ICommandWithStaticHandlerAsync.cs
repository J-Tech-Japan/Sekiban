using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandWithStaticHandlerAsync<TAggregatePayload, in TCommand> : ICommandWithStaticHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{

    public static abstract Task<ResultBox<UnitValue>> HandleCommandAsync(TCommand command, ICommandContext<TAggregatePayload> context);
}