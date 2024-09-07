using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandler<TAggregatePayload, in TCommand> : ICommandWithHandler<TAggregatePayload, TCommand>,
    ITenantCommandNextCommon<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ITenantCommandWithHandler<TAggregatePayload, TCommand>
{
    public static new string GetRootPartitionKey(TCommand command) =>
        TCommand.GetTenantId(command);
}
