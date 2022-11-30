using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommand<TAggregatePayload> : ICommandCommon where TAggregatePayload : IAggregatePayload
{
    public Guid GetAggregateId();
}
