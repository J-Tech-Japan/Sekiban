using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.Query;

public class QueryExecutor
{

    public Task<ResultBox<ListQueryResult<TOutput>>>
        ExecuteListWithMultiProjectionFunction<TMultiProjector, TQuery, TOutput>(
            TQuery query,
            Func<MultiProjectionState<TMultiProjector>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> filter,
            Func<IEnumerable<TOutput>, TQuery, IQueryContext, ResultBox<IEnumerable<TOutput>>> sort)
        where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
        where TMultiProjector : IMultiProjector<TMultiProjector> => Repository
        .LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All)
        .Combine(_ => new QueryContext().ToResultBox())
        .Combine((projection, context) => filter(projection, query, context))
        .Conveyor((_, context, filtered) => SortAndReturnQuery(query, sort, filtered, context));
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
        where TOutput : notnull =>
        sort(filtered, query, context)
            .Remap(enumerable => enumerable.ToList())
            .Remap(sorted => CreateListQueryResult(query, sorted))
            .ToTask();
}
