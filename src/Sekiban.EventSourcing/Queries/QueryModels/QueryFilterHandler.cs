using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public class QueryFilterHandler
{
    private readonly IServiceProvider _serviceProvider;

    public QueryFilterHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IEnumerable<TQueryFilterResponse>
        GetProjectionListQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            MultipleAggregateProjectionContentsDto<TProjectionContents> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new
        ()
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projection);
        var sorted = queryFilter.HandleSort(param, filtered);
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return sorted.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return sorted;
    }

    public TQueryFilterResponse GetProjectionQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
        TQueryFilterParameter param,
        MultipleAggregateProjectionContentsDto<TProjectionContents> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projection);
        return queryFilter.HandleSortAndPagingIfNeeded(param, filtered);
    }


    public IEnumerable<TQueryFilterResponse>
        GetAggregateListQueryFilter<TAggregate, TAggregateContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<AggregateDto<TAggregateContents>> list) where TAggregate : TransferableAggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, list);
        var sorted = queryFilter.HandleSort(param, filtered);
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return sorted.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return sorted;
    }
    public IEnumerable<TQueryFilterResponse>
        GetSingleAggregateProjectionListQueryFilter<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>> projections) where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projections);
        var sorted = queryFilter.HandleSort(param, filtered);

        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return sorted.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return sorted;
    }
}
