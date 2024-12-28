using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerAsyncAbove<TAggregatePayload, TCommand>,
    ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
    where TCommand : ICommandCommon<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
