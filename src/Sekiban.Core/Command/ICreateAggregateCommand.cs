using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICreateAggregateCommand<T> : IAggregateCommand where T : IAggregatePayload
{
}
