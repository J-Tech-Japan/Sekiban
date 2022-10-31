using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICreateCommand<T> : ICommand where T : IAggregatePayload
{
    public Guid GetAggregateId();
}
