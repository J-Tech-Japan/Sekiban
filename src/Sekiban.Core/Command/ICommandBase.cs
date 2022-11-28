using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandBase<TAggregatePayload> : ICommand where TAggregatePayload : IAggregatePayload
{
    public Guid GetAggregateId();
}
