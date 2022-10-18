using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleAggregate;

public class DefaultSingleAggregateProjector<T> : ISingleAggregateProjector<T> where T : AggregateCommonBase
{
    public T CreateInitialAggregate(Guid aggregateId)
    {
        return AggregateCommonBase.Create<T>(aggregateId);
    }
    public Type OriginalAggregateType()
    {
        return typeof(T);
    }
}
