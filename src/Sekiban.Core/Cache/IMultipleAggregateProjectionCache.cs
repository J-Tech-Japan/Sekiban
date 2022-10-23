using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.MultipleAggregate.MultipleProjection;
namespace Sekiban.Core.Cache;

public interface IMultipleAggregateProjectionCache
{
    public void Set<TProjection, TContents>(MultipleMemoryProjectionContainer<TProjection, TContents> container)
        where TProjection : IMultipleAggregateProjector<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new();
    public MultipleMemoryProjectionContainer<TProjection, TContents> Get<TProjection, TContents>()
        where TProjection : IMultipleAggregateProjector<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new();
}
