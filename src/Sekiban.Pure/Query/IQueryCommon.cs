using ResultBoxes;
using Sekiban.Pure.Exception;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Types;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Query;

public interface IQueryCommon<TOutput> where TOutput : notnull;
public interface IListQueryCommon<TOutput> where TOutput : notnull;
public interface IListQueryCommon<TQuery, TOutput> : IListQueryCommon<TOutput>
    where TQuery : IListQueryCommon<TQuery, TOutput>, IEquatable<TQuery> where TOutput : notnull;
public interface IMultiProjectionQueryCommon<TMultiProjector> where TMultiProjector : IMultiProjector<TMultiProjector>;
public interface
    IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> : IMultiProjectionQueryCommon<TMultiProjector>,
    IListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
{
    public static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(
        MultiProjectionState<TMultiProjector> projection,
        TQuery query,
        IQueryContext context);
    public static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
public interface IQueryContext;
public record QueryContext : IQueryContext;
public class QueryExecutor
{
    [RequiresDynamicCode("The query type is used to determine the execution logic.")]
    public static async Task<ResultBox<ListQueryResult<TOutput>>> Execute<TOutput>(IListQueryCommon<TOutput> query)
        where TOutput : notnull
    {
        var queryType = query.GetType();
        if (queryType.DoesImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>)))
        {
            var method = typeof(QueryExecutor)
                .GetMethodFlex(nameof(ExecuteMultiProjectionQuery))
                ?.MakeGenericMethod(
                    queryType,
                    typeof(TOutput),
                    queryType
                        .GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>))
                        .GetGenericArguments()[0]);

            if (method != null)
            {
                try
                {
                    return await (Task<ResultBox<ListQueryResult<TOutput>>>)method.Invoke(
                        null,
                        new object[] { query })!;
                }
                catch (System.Exception e)
                {
                    return ResultBox.Error<ListQueryResult<TOutput>>(e);
                }
            }
        }

        return ResultBox.Error<ListQueryResult<TOutput>>(
            new NotImplementedException("Default execution logic is not implemented."));
    }

    public static Task<ResultBox<ListQueryResult<TOutput>>>
        ExecuteMultiProjectionQuery<TQuery, TOutput, TMultiProjector>(TQuery query)
        where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull
        where TMultiProjector : IMultiProjector<TMultiProjector> => Repository
        .LoadMultiProjection<TMultiProjector>(MultiProjectionEventSelector.All)
        .Combine(_ => new QueryContext().ToResultBox())
        .Combine((projection, context) => TQuery.HandleFilter(projection, query, context))
        .Conveyor((projection, context, filtered) => TQuery.HandleSort(filtered, query, context))
        .Remap(enumerable => enumerable.ToList())
        .Remap(
            sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                ? ListQueryResult<TOutput>.MakeQueryListResult(pagingParam, sorted)
                : new ListQueryResult<TOutput>(sorted.Count, null, null, null, sorted))
        .ToTask();
}
/// <summary>
///     Query Result for List Query.
///     Query result for the paging query can use generic type to specify the type of the item.
/// </summary>
/// <param name="TotalCount"></param>
/// <param name="TotalPages"></param>
/// <param name="CurrentPage"></param>
/// <param name="PageSize"></param>
/// <param name="Items"></param>
/// <typeparam name="T"></typeparam>
public record ListQueryResult<T>(
    int? TotalCount,
    int? TotalPages,
    int? CurrentPage,
    int? PageSize,
    IEnumerable<T> Items)
{
    public static ListQueryResult<T> Empty => new(0, 0, 0, 0, Array.Empty<T>());

    public virtual bool Equals(ListQueryResult<T>? other) =>
        other != null &&
        TotalCount == other.TotalCount &&
        TotalPages == other.TotalPages &&
        CurrentPage == other.CurrentPage &&
        PageSize == other.PageSize &&
        Items.SequenceEqual(other.Items);
    internal static ListQueryResult<T> MakeQueryListResult(
        IQueryPagingParameterCommon pagingParam,
        List<T> queryResponses)
    {
        if (pagingParam.PageNumber == null || pagingParam.PageSize == null)
        {
            throw new SekibanQueryPagingError();
        }
        var pageNumber = pagingParam.PageNumber.Value;
        var pageSize = pagingParam.PageSize.Value;
        var total = queryResponses.ToList().Count;
        var totalPages = total / pagingParam.PageSize.Value + (total % pagingParam.PageSize.Value > 0 ? 1 : 0);
        return pageNumber < 1 || pageNumber > totalPages
            ? new ListQueryResult<T>(total, totalPages, pageNumber, pageSize, new List<T>())
            : new ListQueryResult<T>(
                total,
                totalPages,
                pageNumber,
                pageSize,
                queryResponses.Skip((pageNumber - 1) * pagingParam.PageSize.Value).Take(pageSize));
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = TotalCount.GetHashCode();
            hashCode = hashCode * 397 ^ TotalPages.GetHashCode();
            hashCode = hashCode * 397 ^ CurrentPage.GetHashCode();
            hashCode = hashCode * 397 ^ PageSize.GetHashCode();
            hashCode = hashCode * 397 ^ Items.GetHashCode();
            return hashCode;
        }
    }
}
public interface IQueryPagingParameterCommon
{
    [Range(1, int.MaxValue)]
    public int? PageSize { get; }

    [Range(1, int.MaxValue)]
    public int? PageNumber { get; }
}
