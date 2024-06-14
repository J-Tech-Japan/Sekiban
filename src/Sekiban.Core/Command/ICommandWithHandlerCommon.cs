using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandWithHandlerCommon<TAggregatePayload, in TCommand> : ICommand<TAggregatePayload>, ICommandWithHandlerCommon
    where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>;
public interface ICommandWithHandlerCommon;
