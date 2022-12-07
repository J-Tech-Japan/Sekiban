using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Query.QueryModel;

public class QueryExecutor : IQueryExecutor
{
    private readonly IMultiProjectionService multiProjectionService;
    private readonly QueryHandler queryHandler;
    private readonly IServiceProvider serviceProvider;
    public QueryExecutor(IMultiProjectionService multiProjectionService, QueryHandler queryHandler, IServiceProvider serviceProvider)
    {
        this.multiProjectionService = multiProjectionService;
        this.queryHandler = queryHandler;
        this.serviceProvider = serviceProvider;
    }

    public async Task<TQueryResponse>
        ForMultiProjectionQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }

    public async Task<ListQueryResult<TQueryResponse>>
        ForMultiProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }

    public async Task<ListQueryResult<TQueryResponse>>
        ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>();
        return queryHandler
            .GetAggregateListQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }

    public async Task<TQueryResponse>
        ForAggregateQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>();
        return queryHandler.GetAggregateQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }

    public async Task<ListQueryResult<TQueryResponse>>
        ForSingleProjectionListQueryAsync<TProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TProjectionPayload>();
        return queryHandler
            .GetSingleProjectionListQuery<TProjectionPayload, TQuery,
                TQueryParameter, TQueryResponse>(param, allProjection);
    }

    public async Task<TQueryResponse>
        ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TSingleProjectionPayload>();
        return queryHandler
            .GetSingleProjectionQuery<TSingleProjectionPayload, TQuery,
                TQueryParameter, TQueryResponse>(param, allProjection);
    }
    public async Task<ListQueryResult<TOutput>> ExecuteAsync<TOutput>(IListQueryInput<TOutput> param) where TOutput : IQueryResponse
    {
        var paramType = param.GetType();
        if (!paramType.IsListQueryInputType()) { throw new Exception("Invalid parameter type"); }
        var outputType = paramType.GetOutputClassFromListQueryInputType();
        var handler = serviceProvider.GetQueryObjectFromQueryInputType(paramType, outputType) ??
            throw new Exception("Can not find query handler for" + paramType.Name);
        var handlerType = (Type)handler.GetType();
        if (handlerType.IsAggregateListQueryType())
        {
            var aggregateType = handlerType.GetAggregateTypeFromAggregateListQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForAggregateListQueryAsync)) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync");
            var method = (MethodInfo?)baseMethod.MakeGenericMethod(aggregateType, handler.GetType(), paramType, outputType) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, new object?[] { param }) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        if (handlerType.IsSingleProjectionListQueryType())
        {
            var singleProjectionType = handlerType.GetSingleProjectionTypeFromSingleProjectionListQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForSingleProjectionListQueryAsync)) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync");
            var method = (MethodInfo?)baseMethod.MakeGenericMethod(singleProjectionType, handler.GetType(), paramType, outputType) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, new object?[] { param }) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        if (handlerType.IsMultiProjectionListQueryType())
        {
            var multiProjectionType = handlerType.GetMultiProjectionTypeFromMultiProjectionListQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForMultiProjectionListQueryAsync)) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync");
            var method = (MethodInfo?)baseMethod.MakeGenericMethod(multiProjectionType, handler.GetType(), paramType, outputType) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, new object?[] { param }) ??
                throw new Exception("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        throw new Exception("Can not find query handler for" + paramType.Name);
    }
    public Task<TOutput> ExecuteAsync<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryResponse => throw new NotImplementedException();
}
