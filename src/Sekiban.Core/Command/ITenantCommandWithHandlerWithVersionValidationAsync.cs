using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface
    ITenantCommandWithHandlerWithVersionValidationAsync<TAggregatePayload, in TCommand> :
    ICommandWithHandlerWithVersionValidationAsync<TAggregatePayload, TCommand>,
    ITenantCommandCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
