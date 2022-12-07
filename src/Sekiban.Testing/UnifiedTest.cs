using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Testing.Command;
using Sekiban.Testing.Projection;
using Sekiban.Testing.SingleProjections;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing;

public abstract class UnifiedTest<TDependencyDefinition> where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    private readonly TestEventHandler _eventHandler;
    protected readonly IServiceProvider _serviceProvider;

    // ReSharper disable once PublicConstructorInAbstractClass
    public UnifiedTest()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
        _eventHandler = new TestEventHandler(_serviceProvider);
    }

    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {
    }

    #region Get Aggregate Test
    public AggregateTest<TAggregatePayload, TDependencyDefinition> GetAggregateTest<TAggregatePayload>(Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new() => new(_serviceProvider, aggregateId);

    public AggregateTest<TAggregatePayload, TDependencyDefinition> GetAggregateTest<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload, new() => new(_serviceProvider);

    public UnifiedTest<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Guid aggregateId,
        Action<AggregateTest<TAggregatePayload, TDependencyDefinition>> testAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        testAction(new AggregateTest<TAggregatePayload, TDependencyDefinition>(_serviceProvider, aggregateId));
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Action<AggregateTest<TAggregatePayload, TDependencyDefinition>> testAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        testAction(new AggregateTest<TAggregatePayload, TDependencyDefinition>(_serviceProvider));
        return this;
    }
    #endregion

    #region Aggregate Query
    private TQueryResponse GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ForAggregateQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)
                .Result ??
            throw new Exception("Failed to get Aggregate Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTest<TDependencyDefinition> WriteAggregateQueryResponseToFile<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(
            GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateQueryResponseIs<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var actual = GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetAggregateQueryResponse<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        responseAction(GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)!);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateQueryResponseIsFromJson<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenAggregateQueryResponseIs<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateQueryResponseIsFromFile<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenAggregateQueryResponseIs<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    #endregion

    #region Aggregate　List Query
    private ListQueryResult<TQueryResponse> GetAggregateListQueryResponse<TAggregatePayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService
                .ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)
                .Result ??
            throw new Exception("Failed to get Aggregate List Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTest<TDependencyDefinition> WriteAggregateListQueryResponseToFile<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(
            GetAggregateListQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateListQueryResponseIs<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var actual = GetAggregateListQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetAggregateListQueryResponse<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        responseAction(
            GetAggregateListQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)!);
        return this;
    }

    public UnifiedTest<TDependencyDefinition>
        ThenAggregateListQueryResponseIsFromJson<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            string responseJson)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenAggregateListQueryResponseIs<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition>
        ThenAggregateListQueryResponseIsFromFile<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            string responseFilename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenAggregateListQueryResponseIs<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }
    #endregion

    #region SingleProjection Query
    private TQueryResponse GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService
                .ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery, TQueryParameter,
                    TQueryResponse>(param)
                .Result ??
            throw new Exception("Failed to get Single Projection Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTest<TDependencyDefinition> WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(
            GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var actual =
            GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        responseAction(
            GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }
    #endregion

    #region SingleProjection　List Query
    private ListQueryResult<TQueryResponse> GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var singleProjection = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return singleProjection
                .ForSingleProjectionListQueryAsync<TSingleProjectionPayload, TQuery, TQueryParameter,
                    TQueryResponse>(param)
                .Result ??
            throw new Exception("Failed to get Single Projection Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTest<TDependencyDefinition> WriteSingleProjectionListQueryResponseToFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(
            GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var actual =
            GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        responseAction(
            GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListQueryResponseIsFromJson<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListQueryResponseIsFromFile<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }
    #endregion

    #region Multi Projection Query
    private TQueryResponse GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(TQueryParameter param)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService
                .ForMultiProjectionQueryAsync<TMultiProjectionPayload, TQuery, TQueryParameter,
                    TQueryResponse>(param)
                .Result ??
            throw new Exception("Failed to get Multi Projection Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTest<TDependencyDefinition> WriteMultiProjectionQueryResponseToFile<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(
            GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionQueryResponseIs<TMultiProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var actual =
            GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        responseAction(
            GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionQueryResponseIsFromJson<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenMultiProjectionQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionQueryResponseIsFromFile<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);

        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenMultiProjectionQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }
    #endregion

    #region Multi Projection　List Query
    private ListQueryResult<TQueryResponse> GetMultiProjectionListQueryResponse<TMultiProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService
                .ForMultiProjectionListQueryAsync<TMultiProjectionPayload, TQuery, TQueryParameter,
                    TQueryResponse>(param)
                .Result ??
            throw new Exception("Failed to get Multi Projection List Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTest<TDependencyDefinition> WriteMultiProjectionListQueryResponseToFile<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(
            GetMultiProjectionListQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionListQueryResponseIs<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var actual =
            GetMultiProjectionListQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetMultiProjectionListQueryResponse<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        responseAction(
            GetMultiProjectionListQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionListQueryResponseIsFromJson<
        TMultiProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenMultiProjectionListQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionListQueryResponseIsFromFile<
        TMultiProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenMultiProjectionListQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            response);
        return this;
    }
    #endregion

    #region Multi Projection
    public MultiProjectionState<TMultiProjectionPayload> GetMultiProjectionState<TMultiProjectionPayload>()
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
            throw new Exception("Failed to get Query service");
        return multiProjectionService.GetMultiProjectionAsync<TMultiProjectionPayload>().Result ??
            throw new Exception(
                "Failed to get Multi Projection Response for " + typeof(TMultiProjectionPayload).Name);
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionPayloadIsFromFile<TMultiProjectionPayload>(
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<TMultiProjectionPayload>(openStream);
        if (projection is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        return ThenMultiProjectionPayloadIs(projection);
    }

    public UnifiedTest<TDependencyDefinition> ThenGetMultiProjectionPayload<TMultiProjectionPayload>(
        Action<TMultiProjectionPayload> payloadAction)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        payloadAction(GetMultiProjectionState<TMultiProjectionPayload>().Payload);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetMultiProjectionState<TMultiProjectionPayload>(
        Action<MultiProjectionState<TMultiProjectionPayload>> stateAction)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        stateAction(GetMultiProjectionState<TMultiProjectionPayload>());
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionStateIs<TMultiProjectionPayload>(
        MultiProjectionState<TMultiProjectionPayload> state)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var actual = GetMultiProjectionState<TMultiProjectionPayload>();
        var expected = state with
        {
            LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId,
            Version = actual.Version
        };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionPayloadIs<TMultiProjectionPayload>(
        TMultiProjectionPayload payload)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var actual = GetMultiProjectionState<TMultiProjectionPayload>().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionStateIsFromFile<TMultiProjectionPayload>(
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<MultiProjectionState<TMultiProjectionPayload>>(openStream);
        if (projection is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        return ThenMultiProjectionStateIs(projection);
    }

    public UnifiedTest<TDependencyDefinition> WriteMultiProjectionStateToFile<TMultiProjectionPayload>(
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var json = SekibanJsonHelper.Serialize(GetMultiProjectionState<TMultiProjectionPayload>());
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    #endregion

    #region Aggregate List Projection
    public MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>
        GetAggregateListProjectionState<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload, new()
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
            throw new Exception("Failed to get Query service");
        return multiProjectionService.GetAggregateListObject<TAggregatePayload>().Result ??
            throw new Exception(
                "Failed to get Aggregate List Projection Response for " +
                typeof(TAggregatePayload).Name);
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionPayloadIsFromFile<TAggregatePayload>(
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer.Deserialize<SingleProjectionListState<AggregateState<TAggregatePayload>>>(openStream);
        if (projection is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        return ThenAggregateListProjectionPayloadIs(projection);
    }

    public UnifiedTest<TDependencyDefinition> ThenGetAggregateListProjectionPayload<TAggregatePayload>(
        Action<SingleProjectionListState<AggregateState<TAggregatePayload>>> payloadAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        payloadAction(GetAggregateListProjectionState<TAggregatePayload>().Payload);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetAggregateListProjectionState<TAggregatePayload>(
        Action<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> stateAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        stateAction(GetAggregateListProjectionState<TAggregatePayload>());
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionStateIs<TAggregatePayload>(
        MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>> state)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var actual = GetAggregateListProjectionState<TAggregatePayload>();
        var expected = state with
        {
            LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId,
            Version = actual.Version
        };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionPayloadIs<TAggregatePayload>(
        SingleProjectionListState<AggregateState<TAggregatePayload>> payload)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var actual = GetAggregateListProjectionState<TAggregatePayload>().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionStateIsFromFile<TAggregatePayload>(
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer
                .Deserialize<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>(
                    openStream);
        if (projection is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        return ThenAggregateListProjectionStateIs(projection);
    }

    public UnifiedTest<TDependencyDefinition> WriteAggregateListProjectionStateToFile<TAggregatePayload>(
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var json = SekibanJsonHelper.Serialize(GetAggregateListProjectionState<TAggregatePayload>());
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    #endregion

    #region Single Projection List Projection
    public MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionListState<
            TSingleProjectionPayload>()
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
            throw new Exception("Failed to get Query service");
        return multiProjectionService.GetSingleProjectionListObject<TSingleProjectionPayload>().Result ??
            throw new Exception(
                "Failed to get Single Projection List Projection Response for " +
                typeof(TSingleProjectionPayload).Name);
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListPayloadIsFromFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer.Deserialize<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(
                openStream);
        if (projection is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        return ThenSingleProjectionListPayloadIs(projection);
    }

    public UnifiedTest<TDependencyDefinition> ThenGetSingleProjectionListPayload<TSingleProjectionPayload>(
        Action<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> payloadAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        payloadAction(GetSingleProjectionListState<TSingleProjectionPayload>().Payload);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenGetSingleProjectionListState<TSingleProjectionPayload>(
        Action<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
            stateAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        stateAction(GetSingleProjectionListState<TSingleProjectionPayload>());
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListStateIs<TSingleProjectionPayload>(
        MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> state)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var actual = GetAggregateListProjectionState<TSingleProjectionPayload>();
        var expected = state with
        {
            LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId,
            Version = actual.Version
        };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListPayloadIs<TSingleProjectionPayload>(
        SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>> payload)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var actual = GetSingleProjectionListState<TSingleProjectionPayload>().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListStateIsFromFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer
                .Deserialize<
                    MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>(
                    openStream);
        if (projection is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        return ThenSingleProjectionListStateIs(projection);
    }

    public UnifiedTest<TDependencyDefinition> WriteSingleProjectionListStateToFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var json = SekibanJsonHelper.Serialize(GetSingleProjectionListState<TSingleProjectionPayload>());
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    #endregion

    #region Given
    public UnifiedTest<TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }

    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(
        Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
        if (singleProjectionService is null)
        {
            throw new Exception("Failed to get single aggregate service");
        }
        var aggregate = singleProjectionService.AsDefaultStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ??
            throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }

    public IReadOnlyCollection<IEvent> GetLatestEvents() => _commandExecutor.LatestEvents;

    public IReadOnlyCollection<IEvent> GetAllAggregateEvents<TAggregatePayload>(Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new() => _commandExecutor.GetAllAggregateEvents<TAggregatePayload>(aggregateId);

    #region GivenEvents
    public UnifiedTest<TDependencyDefinition> GivenEvents(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEvents(events);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEventsWithPublish(events);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEvents(params IEvent[] events) => GivenEvents(events.AsEnumerable());

    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(params IEvent[] events) => GivenEventsWithPublish(events.AsEnumerable());

    public UnifiedTest<TDependencyDefinition> GivenEventsFromJson(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJson(jsonEvents);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEventsFromJsonWithPublish(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJsonWithPublish(jsonEvents);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEvents(eventTouples);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEventsWithPublish(eventTouples);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEvents(eventTouples);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEventsWithPublish(eventTouples);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEventsFromFile(string filename)
    {
        _eventHandler.GivenEventsFromFile(filename);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> GivenEventsFromFileWithPublish(string filename)
    {
        _eventHandler.GivenEventsFromFileWithPublish(filename);
        return this;
    }
    #endregion


    #region Run Commands
    public Guid RunCommand<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new() => _commandExecutor.ExecuteCommand(command, injectingAggregateId);

    public Guid RunCommandWithPublish<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new() => _commandExecutor.ExecuteCommandWithPublish(command, injectingAggregateId);

    public UnifiedTest<TDependencyDefinition> GivenCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }
    #endregion
    #endregion
}
