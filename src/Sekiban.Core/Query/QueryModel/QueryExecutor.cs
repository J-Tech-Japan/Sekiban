using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using Sekiban.Core.Validation;
using System.Reflection;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Executor implementation.
///     Developers use <see cref="IQueryExecutor" /> interface to execute query.
/// </summary>
public class QueryExecutor : IQueryExecutor
{
    private readonly IMultiProjectionService multiProjectionService;
    private readonly QueryHandler queryHandler;
    private readonly IServiceProvider serviceProvider;
    public QueryExecutor(
        IMultiProjectionService multiProjectionService,
        QueryHandler queryHandler,
        IServiceProvider serviceProvider)
    {
        this.multiProjectionService = multiProjectionService;
        this.queryHandler = queryHandler;
        this.serviceProvider = serviceProvider;
    }
    public async Task<ResultBox<TOutput>> ExecuteAsync<TOutput>(INextQueryCommonOutput<TOutput> query)
        where TOutput : notnull
    {
        var validationResult = query.ValidateProperties().ToList();
        if (validationResult.Count != 0)
        {
            return new SekibanValidationErrorsException(validationResult);
        }
        var paramType = query.GetType();
        if (!paramType.IsQueryNextType()) { throw new SekibanQueryExecutionException("Invalid parameter type"); }
        var outputType = paramType.GetOutputClassFromNextQueryType();
        switch (query)
        {
            case not null when paramType.IsAggregateQueryNextType():
            {
                var aggregateType = paramType.GetAggregateTypeFromNextAggregateQueryType();
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForAggregateQueryNextAsync)),
                    not null when !paramType.IsNextQueryAsync() => GetType().GetMethod(nameof(ForAggregateQueryNext)),
                    _ => throw new SekibanQueryExecutionException("Can not find method ForAggregateQueryNextAsync")
                };
                var method = baseMethod?.MakeGenericMethod(aggregateType, paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method ForAggregateQueryNextAsync");
                var result = await (dynamic)(method.Invoke(this, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method ForAggregateQueryNextAsync"));
                return result;
            }
            case not null when paramType.IsSingleProjectionNextQueryType():
            {
                var singleProjectionType = paramType.GetSingleProjectionTypeFromNextSingleProjectionQueryType();
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForSingleProjectionNextQueryAsync)),
                    not null when !paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForSingleProjectionNextQuery)),
                    _ => throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery")
                };
                var method = baseMethod?.MakeGenericMethod(singleProjectionType, paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery");
                var result = await (dynamic)(method.Invoke(this, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery"));
                return result;
            }
            case not null when paramType.IsMultiProjectionNextQueryType():
            {
                var multiProjectionType = paramType.GetMultiProjectionTypeFromMultiProjectionNextQueryType();
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForMultiProjectionQueryNextAsync)),
                    not null when !paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForMultiProjectionQueryNext)),
                    _ => throw new SekibanQueryExecutionException("Can not find method ForMultiProjectionQueryNext")
                };
                var method = baseMethod?.MakeGenericMethod(multiProjectionType, paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method ForMultiProjectionQueryNext");
                var result = await (dynamic)(method.Invoke(this, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method ForMultiProjectionQueryNext"));
                return result;
            }
            case not null when paramType.IsNextGeneralQueryType():
            {
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => typeof(QueryHandler).GetMethod(
                        nameof(QueryHandler.GetGeneralQueryNextAsync)),
                    not null when !paramType.IsNextQueryAsync() => typeof(QueryHandler).GetMethod(
                        nameof(QueryHandler.GetGeneralQueryNext)),
                    _ => throw new SekibanQueryExecutionException("Can not find method GetGeneralQueryNext")
                };
                var method = baseMethod?.MakeGenericMethod(paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method GetGeneralQueryNext");
                var result = await (dynamic)(method.Invoke(queryHandler, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method GetGeneralQueryNext"));
                return result;
            }
        }
        throw new SekibanQueryExecutionException("Can not find query handler for" + paramType.Name);
    }
    public async Task<ResultBox<ListQueryResult<TOutput>>> ExecuteAsync<TOutput>(
        INextListQueryCommonOutput<TOutput> query) where TOutput : notnull
    {
        var validationResult = query.ValidateProperties().ToList();
        if (validationResult.Count != 0)
        {
            return new SekibanValidationErrorsException(validationResult);
        }
        var paramType = query.GetType();
        if (!paramType.IsListQueryNextType())
        {
            throw new SekibanQueryExecutionException("Invalid parameter type");
        }
        var outputType = paramType.GetOutputClassFromNextQueryType();
        switch (query)
        {
            case not null when paramType.IsAggregateQueryNextType():
            {
                var aggregateType = paramType.GetAggregateTypeFromNextAggregateQueryType();
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForAggregateListQueryNextAsync)),
                    not null when !paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForAggregateListQueryNext)),
                    _ => throw new SekibanQueryExecutionException("Can not find method ForAggregateQueryNextAsync")
                };
                var method = baseMethod?.MakeGenericMethod(aggregateType, paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method ForAggregateQueryNextAsync");
                var result = await (dynamic)(method.Invoke(this, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method ForAggregateQueryNextAsync"));
                return result;
            }
            case not null when paramType.IsSingleProjectionNextQueryType():
            {
                var singleProjectionType = paramType.GetSingleProjectionTypeFromNextSingleProjectionQueryType();
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForSingleProjectionListQueryNextAsync)),
                    not null when !paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForSingleProjectionListQueryNext)),
                    _ => throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery")
                };
                var method = baseMethod?.MakeGenericMethod(singleProjectionType, paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery");
                var result = await (dynamic)(method.Invoke(this, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery"));
                return result;
            }
            case not null when paramType.IsMultiProjectionNextQueryType():
            {
                var multiProjectionType = paramType.GetMultiProjectionTypeFromMultiProjectionNextQueryType();
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForMultiProjectionListQueryNextAsync)),
                    not null when !paramType.IsNextQueryAsync() => GetType()
                        .GetMethod(nameof(ForMultiProjectionListQueryNext)),
                    _ => throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery")
                };
                var method = baseMethod?.MakeGenericMethod(multiProjectionType, paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery");
                var result = await (dynamic)(method.Invoke(this, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method ForSingleProjectionNextQuery"));
                return result;
            }
            case not null when paramType.IsNextGeneralQueryType():
            {
                var baseMethod = paramType switch
                {
                    not null when paramType.IsNextQueryAsync() => typeof(QueryHandler).GetMethod(
                        nameof(QueryHandler.GetGeneralListQueryNextAsync)),
                    not null when !paramType.IsNextQueryAsync() => typeof(QueryHandler).GetMethod(
                        nameof(QueryHandler.GetGeneralListQueryNext)),
                    _ => throw new SekibanQueryExecutionException("Can not find method GetGeneralListQueryNext")
                };
                var method = baseMethod?.MakeGenericMethod(paramType, outputType) ??
                    throw new SekibanQueryExecutionException("Can not find method GetGeneralListQueryNext");
                var result = await (dynamic)(method.Invoke(queryHandler, [query]) ??
                    throw new SekibanQueryExecutionException("Can not find method GetGeneralListQueryNext"));
                return result;
            }
        }
        throw new SekibanQueryExecutionException("Can not find query handler for" + paramType.Name);
    }
    public async Task<ListQueryResult<TOutput>> ExecuteAsync<TOutput>(IListQueryInput<TOutput> param)
        where TOutput : IQueryResponse
    {
        var validationResult = param.ValidateProperties().ToList();
        if (validationResult.Count != 0)
        {
            throw new SekibanValidationErrorsException(validationResult);
        }
        var paramType = param.GetType();
        if (!paramType.IsListQueryInputType()) { throw new SekibanQueryExecutionException("Invalid parameter type"); }
        var outputType = paramType.GetOutputClassFromListQueryInputType();
        var handler = serviceProvider.GetQueryObjectFromListQueryInputType(paramType, outputType) ??
            throw new SekibanQueryExecutionException("Can not find query handler for" + paramType.Name);
        var handlerType = (Type)handler.GetType();
        if (handlerType.IsAggregateListQueryType())
        {
            var aggregateType = handlerType.GetAggregateTypeFromAggregateListQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForAggregateListQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var method
                = (MethodInfo?)baseMethod.MakeGenericMethod(aggregateType, handler.GetType(), paramType, outputType) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        if (handlerType.IsSingleProjectionListQueryType())
        {
            var singleProjectionType = handlerType.GetSingleProjectionTypeFromSingleProjectionListQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForSingleProjectionListQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var method
                = (MethodInfo?)baseMethod.MakeGenericMethod(
                    singleProjectionType,
                    handler.GetType(),
                    paramType,
                    outputType) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        if (handlerType.IsMultiProjectionListQueryType())
        {
            var multiProjectionType = handlerType.GetMultiProjectionTypeFromMultiProjectionListQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForMultiProjectionListQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var method
                = (MethodInfo?)baseMethod.MakeGenericMethod(
                    multiProjectionType,
                    handler.GetType(),
                    paramType,
                    outputType) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        if (handlerType.IsGeneralListQueryType())
        {
            var baseMethod = GetType().GetMethod(nameof(ForGeneralListQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method ForGeneralListQueryAsync");
            var method = (MethodInfo?)baseMethod.MakeGenericMethod(handler.GetType(), paramType, outputType) ??
                throw new SekibanQueryExecutionException("Can not find method ForGeneralListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method ForGeneralListQueryAsync"));
            return result;
        }
        throw new SekibanQueryExecutionException("Can not find query handler for" + paramType.Name);
    }
    public async Task<TOutput> ExecuteAsync<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryResponse
    {
        var validationResult = param.ValidateProperties().ToList();
        if (validationResult.Count != 0)
        {
            throw new SekibanValidationErrorsException(validationResult);
        }
        var paramType = param.GetType();
        if (!paramType.IsQueryInputType()) { throw new SekibanQueryExecutionException("Invalid parameter type"); }
        var outputType = paramType.GetOutputClassFromQueryInputType();
        var handler = serviceProvider.GetQueryObjectFromQueryInputType(paramType, outputType) ??
            throw new SekibanQueryExecutionException("Can not find query handler for " + paramType.Name);
        var handlerType = (Type)handler.GetType();
        if (handlerType.IsAggregateQueryType())
        {
            var aggregateType = handlerType.GetAggregateTypeFromAggregateQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForAggregateQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var method
                = (MethodInfo?)baseMethod.MakeGenericMethod(aggregateType, handler.GetType(), paramType, outputType) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        if (handlerType.IsSingleProjectionQueryType())
        {
            var singleProjectionType = handlerType.GetSingleProjectionTypeFromSingleProjectionQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForSingleProjectionQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var method
                = (MethodInfo?)baseMethod.MakeGenericMethod(
                    singleProjectionType,
                    handler.GetType(),
                    paramType,
                    outputType) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method ForAggregateListQueryAsync"));
            return result;
        }
        if (handlerType.IsMultiProjectionQueryType())
        {
            var multiProjectionType = handlerType.GetMultiProjectionTypeFromMultiProjectionQueryType();
            var baseMethod = GetType().GetMethod(nameof(ForMultiProjectionQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method For MultiProjectionQuery");
            var method
                = (MethodInfo?)baseMethod.MakeGenericMethod(
                    multiProjectionType,
                    handler.GetType(),
                    paramType,
                    outputType) ??
                throw new SekibanQueryExecutionException("Can not find method For MultiProjectionQuery");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method For MultiProjectionQuery"));
            return result;
        }
        if (handlerType.IsGeneralQueryType())
        {
            var baseMethod = GetType().GetMethod(nameof(ForGeneralQueryAsync)) ??
                throw new SekibanQueryExecutionException("Can not find method For GeneralQuery");
            var method = (MethodInfo?)baseMethod.MakeGenericMethod(handler.GetType(), paramType, outputType) ??
                throw new SekibanQueryExecutionException("Can not find method For GeneralQuery");
            var result = await (dynamic)(method.Invoke(this, [param]) ??
                throw new SekibanQueryExecutionException("Can not find method For GeneralQuery"));
            return result;
        }
        throw new SekibanQueryExecutionException("Can not find query handler for" + paramType.Name);
    }

    public async Task<TQueryResponse>
        ForMultiProjectionQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>(
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetMultiProjectionQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }



    public Task<ResultBox<TQueryResponse>>
        ForMultiProjectionQueryNextAsync<TProjectionPayload, TQuery, TQueryResponse>(TQuery query)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionQueryAsync<TProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        ResultBox
            .WrapTry(
                () => multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>(
                    query.GetRootPartitionKey(),
                    MultiProjectionRetrievalOptions.GetFromQuery(query)))
            .Conveyor(
                allProjection =>
                    queryHandler.GetMultiProjectionQueryNextAsync<TProjectionPayload, TQuery, TQueryResponse>(
                        query,
                        allProjection));

    public Task<ResultBox<TQueryResponse>>
        ForMultiProjectionQueryNext<TProjectionPayload, TQuery, TQueryResponse>(TQuery query)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionQuery<TProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        ResultBox
            .WrapTry(
                () => multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>(
                    query.GetRootPartitionKey(),
                    MultiProjectionRetrievalOptions.GetFromQuery(query)))
            .Conveyor(
                allProjection =>
                    queryHandler.GetMultiProjectionQueryNext<TProjectionPayload, TQuery, TQueryResponse>(
                        query,
                        allProjection));


    public async Task<ListQueryResult<TQueryResponse>>
        ForMultiProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>(
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }


    public Task<ResultBox<ListQueryResult<TQueryResponse>>>
        ForMultiProjectionListQueryNextAsync<TProjectionPayload, TQuery, TQueryResponse>(TQuery query)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryResponse>,
        IEquatable<TQuery>
        where TQueryResponse : notnull =>
        ResultBox
            .WrapTry(
                () => multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>(
                    query.GetRootPartitionKey(),
                    MultiProjectionRetrievalOptions.GetFromQuery(query)))
            .Conveyor(
                allProjection =>
                    queryHandler.GetMultiProjectionListQueryNextAsync<TProjectionPayload, TQuery, TQueryResponse>(
                        query,
                        allProjection));
    public Task<ResultBox<ListQueryResult<TQueryResponse>>>
        ForMultiProjectionListQueryNext<TProjectionPayload, TQuery, TQueryResponse>(TQuery query)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        ResultBox
            .WrapTry(
                () => multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>(
                    query.GetRootPartitionKey(),
                    MultiProjectionRetrievalOptions.GetFromQuery(query)))
            .Conveyor(
                allProjection =>
                    queryHandler.GetMultiProjectionListQueryNext<TProjectionPayload, TQuery, TQueryResponse>(
                        query,
                        allProjection));


    public async Task<ListQueryResult<TQueryResponse>>
        ForGeneralListQueryAsync<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IGeneralListQuery<TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse =>
        await queryHandler.GetGeneralListQueryAsync<TQuery, TQueryParameter, TQueryResponse>(param);

    public async Task<TQueryResponse>
        ForGeneralQueryAsync<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IGeneralQuery<TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse =>
        await queryHandler.GetGeneralQueryAsync<TQuery, TQueryParameter, TQueryResponse>(param);



    public async Task<ListQueryResult<TQueryResponse>>
        ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>(
            QueryListType.ActiveOnly,
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetAggregateListQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }

    public async Task<TQueryResponse>
        ForAggregateQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>(
            QueryListType.ActiveOnly,
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetAggregateQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }

    public async Task<ResultBox<TQueryResponse>>
        ForAggregateQueryNextAsync<TAggregatePayload, TQuery, TQueryResponse>(TQuery param)
        where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateQueryAsync<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>(
            TQuery.GetQueryListType(param),
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return await queryHandler.GetAggregateQueryNextAsync<TAggregatePayload, TQuery, TQueryResponse>(
            param,
            allProjection);
    }
    public async Task<ResultBox<TQueryResponse>>
        ForAggregateQueryNext<TAggregatePayload, TQuery, TQueryResponse>(TQuery param)
        where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateQuery<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>(
            TQuery.GetQueryListType(param),
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetAggregateQueryNext<TAggregatePayload, TQuery, TQueryResponse>(param, allProjection);
    }
    public async Task<ResultBox<ListQueryResult<TQueryResponse>>>
        ForAggregateListQueryNextAsync<TAggregatePayload, TQuery, TQueryResponse>(TQuery param)
        where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>(
            TQuery.GetQueryListType(param),
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return await queryHandler.GetAggregateListQueryNextAsync<TAggregatePayload, TQuery, TQueryResponse>(
            param,
            allProjection);
    }
    public async Task<ResultBox<ListQueryResult<TQueryResponse>>>
        ForAggregateListQueryNext<TAggregatePayload, TQuery, TQueryResponse>(TQuery param)
        where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateListQuery<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>(
            TQuery.GetQueryListType(param),
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetAggregateListQueryNext<TAggregatePayload, TQuery, TQueryResponse>(param, allProjection);
    }



    public async Task<ListQueryResult<TQueryResponse>>
        ForSingleProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TProjectionPayload : class, ISingleProjectionPayloadCommon
        where TQuery : ISingleProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetSingleProjectionList<TProjectionPayload>(
            QueryListType.ActiveOnly,
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetSingleProjectionListQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }


    public Task<ResultBox<ListQueryResult<TQueryResponse>>>
        ForSingleProjectionListQueryNextAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(TQuery query)
        where TProjectionPayload : class, ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryResponse>,
        IEquatable<TQuery>
        where TQueryResponse : IQueryResponse =>
        ResultBox
            .FromValue(
                multiProjectionService.GetSingleProjectionList<TProjectionPayload>(
                    TQuery.GetQueryListType(query),
                    query.GetRootPartitionKey(),
                    MultiProjectionRetrievalOptions.GetFromQuery(query)))
            .Conveyor(
                allProjection =>
                    queryHandler.GetSingleProjectionListNextQueryAsync<TProjectionPayload, TQuery, TQueryResponse>(
                        query,
                        allProjection));
    public Task<ResultBox<ListQueryResult<TQueryResponse>>>
        ForSingleProjectionListQueryNext<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(TQuery query)
        where TProjectionPayload : class, ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionListQuery<TProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : IQueryResponse =>
        ResultBox
            .FromValue(
                multiProjectionService.GetSingleProjectionList<TProjectionPayload>(
                    TQuery.GetQueryListType(query),
                    query.GetRootPartitionKey(),
                    MultiProjectionRetrievalOptions.GetFromQuery(query)))
            .Conveyor(
                allProjection =>
                    queryHandler.GetSingleProjectionListNextQuery<TProjectionPayload, TQuery, TQueryResponse>(
                        query,
                        allProjection));

    public async Task<TQueryResponse>
        ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var allProjection = await multiProjectionService.GetSingleProjectionList<TSingleProjectionPayload>(
            QueryListType.ActiveOnly,
            param.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(param));
        return queryHandler.GetSingleProjectionQuery<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }


    public async Task<ResultBox<TQueryResponse>>
        ForSingleProjectionNextQueryAsync<TSingleProjectionPayload, TQuery, TQueryResponse>(TQuery query)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery, TQueryResponse>,
        IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var allProjection = await multiProjectionService.GetSingleProjectionList<TSingleProjectionPayload>(
            TQuery.GetQueryListType(query),
            query.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(query));
        return await queryHandler.GetSingleProjectionQueryNextAsync<TSingleProjectionPayload, TQuery, TQueryResponse>(
            query,
            allProjection);
    }
    public async Task<ResultBox<TQueryResponse>>
        ForSingleProjectionNextQuery<TSingleProjectionPayload, TQuery, TQueryResponse>(TQuery query)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionQuery<TSingleProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var allProjection = await multiProjectionService.GetSingleProjectionList<TSingleProjectionPayload>(
            TQuery.GetQueryListType(query),
            query.GetRootPartitionKey(),
            MultiProjectionRetrievalOptions.GetFromQuery(query));
        return queryHandler.GetSingleProjectionQueryNext<TSingleProjectionPayload, TQuery, TQueryResponse>(
            query,
            allProjection);
    }
}
