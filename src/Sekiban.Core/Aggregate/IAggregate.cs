using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Aggregate;

public interface IAggregate : ISingleAggregate, ISingleAggregateProjection
{
}
