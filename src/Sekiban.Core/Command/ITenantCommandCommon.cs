using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ITenantCommandNextCommon<TAggregatePayload, in TCommand> : ICommandCommon<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommandCommon<TAggregatePayload>
{
    public static abstract string GetTenantId(TCommand command);
}
