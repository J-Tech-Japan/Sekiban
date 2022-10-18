using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface IAggregatePointerEvent<TAggregate> where TAggregate : IAggregate
{
}
