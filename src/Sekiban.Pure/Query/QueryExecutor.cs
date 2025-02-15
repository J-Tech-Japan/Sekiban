using ResultBoxes;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Query;

public class QueryExecutor(IServiceProvider serviceProvider)
{
    public Task<ResultBox<ListQueryResult<TOutput>>>
        ExecuteListWithMultiProjectionFunction<TMultiProjector, TQuery, TOutput>(
            TQuery query,
            Func<MultiProjectionState<TMultiProjector>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> filter,
            Func<IEnumerable<TOutput>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> sort,
            Func<IMultiProjectionEventSelector, Task<ResultBox<MultiProjectionState<TMultiProjector>>>>
                repositoryLoader)
        where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
        where TMultiProjector : IMultiProjector<TMultiProjector>
    {
        return repositoryLoader(MultiProjectionEventSelector.All)
            .Combine(_ => new QueryContext(serviceProvider).ToResultBox())
            .Combine((projection, context) => filter(projection, query, context))
            .Conveyor((_, context, filtered) => SortAndReturnQuery(query, sort, filtered, context));
    }

    public Task<ResultBox<TOutput>> ExecuteWithMultiProjectionFunction<TMultiProjector, TQuery, TOutput>(
        TQuery query,
        Func<MultiProjectionState<TMultiProjector>, TQuery, IQueryContext, ResultBox<TOutput>> handler,
        Func<IMultiProjectionEventSelector, Task<ResultBox<MultiProjectionState<TMultiProjector>>>> repositoryLoader)
        where TQuery : IQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
        where TMultiProjector : IMultiProjector<TMultiProjector>
    {
        return repositoryLoader(MultiProjectionEventSelector.All)
            .Combine(_ => new QueryContext(serviceProvider).ToResultBox())
            .Conveyor((projection, context) => handler(projection, query, context));
    }

    private static ListQueryResult<TOutput> CreateListQueryResult<TOutput>(
        IListQueryCommon<TOutput> query,
        List<TOutput> sorted) where TOutput : notnull =>
        query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? ListQueryResult<TOutput>.MakeQueryListResult(pagingParam, sorted)
            : new ListQueryResult<TOutput>(sorted.Count, null, null, null, sorted);

    private Task<ResultBox<ListQueryResult<TOutput>>> SortAndReturnQuery<TQuery, TOutput>(
        TQuery query,
        Func<IEnumerable<TOutput>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> sort,
        IEnumerable<TOutput> filtered,
        IQueryContext context) where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
    {
        return sort(filtered, query, context)
            .Remap(enumerable => enumerable.ToList())
            .Remap(sorted => CreateListQueryResult(query, sorted))
            .ToTask();
    }
}