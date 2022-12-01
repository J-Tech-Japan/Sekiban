using System.Text.Json;
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
using Xunit;

namespace Sekiban.Testing;

public abstract class UnifiedTestBase<TDependencyDefinition> where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    private readonly TestEventHandler _eventHandler;
    protected readonly IServiceProvider _serviceProvider;

    // ReSharper disable once PublicConstructorInAbstractClass
    public UnifiedTestBase()
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

    public AggregateTestBase<TAggregatePayload, TDependencyDefinition> GetAggregateTest<TAggregatePayload>(
        Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new()
    {
        return new(_serviceProvider, aggregateId);
    }

    public AggregateTestBase<TAggregatePayload, TDependencyDefinition> GetAggregateTest<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload, new()
    {
        return new(_serviceProvider);
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Guid aggregateId,
        Action<AggregateTestBase<TAggregatePayload, TDependencyDefinition>> testAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        testAction(new AggregateTestBase<TAggregatePayload, TDependencyDefinition>(_serviceProvider, aggregateId));
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Action<AggregateTestBase<TAggregatePayload, TDependencyDefinition>> testAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        testAction(new AggregateTestBase<TAggregatePayload, TDependencyDefinition>(_serviceProvider));
        return this;
    }

    #endregion

    #region Aggregate Query

    private TQueryResponse GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
                           throw new Exception("Failed to get Query service");
        return queryService.ForAggregateQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)
                   .Result ??
               throw new Exception("Failed to get Aggregate Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteAggregateQueryResponseToFile<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Json is null or empty");
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateQueryResponseIs<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual = GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateQueryResponse<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(GetAggregateQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)!);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateQueryResponseIsFromJson<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenAggregateQueryResponseIs<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateQueryResponseIsFromFile<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
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
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
                           throw new Exception("Failed to get Query service");
        return queryService
                   .ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)
                   .Result ??
               throw new Exception("Failed to get Aggregate List Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteAggregateListQueryResponseToFile<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetAggregateListQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Json is null or empty");
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateListQueryResponseIs<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual = GetAggregateListQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateListQueryResponse<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(
            GetAggregateListQueryResponse<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param)!);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition>
        ThenAggregateListQueryResponseIsFromJson<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            string responseJson)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenAggregateListQueryResponseIs<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition>
        ThenAggregateListQueryResponseIsFromFile<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            string responseFilename)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenAggregateListQueryResponseIs<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(param, response);
        return this;
    }

    #endregion

    #region SingleProjection Query

    private TQueryResponse GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
                           throw new Exception("Failed to get Query service");
        return queryService
                   .ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery, TQueryParameter,
                       TQueryResponse>(param).Result ??
               throw new Exception("Failed to get Single Projection Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Json is null or empty");
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual =
            GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(
            GetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param,
            response);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param,
            response);
        return this;
    }

    #endregion

    #region SingleProjection　List Query

    private ListQueryResult<TQueryResponse> GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var singleProjection = _serviceProvider.GetService<IQueryExecutor>() ??
                               throw new Exception("Failed to get Query service");
        return singleProjection
                   .ForSingleProjectionListQueryAsync<TSingleProjectionPayload, TQuery, TQueryParameter,
                       TQueryResponse>(param).Result ??
               throw new Exception("Failed to get Single Projection Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteSingleProjectionListQueryResponseToFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Json is null or empty");
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
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

    public UnifiedTestBase<TDependencyDefinition> ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(
            GetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionListQueryResponseIsFromJson<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param, response);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionListQueryResponseIsFromFile<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param, response);
        return this;
    }

    #endregion

    #region Multi Projection Query

    private TQueryResponse GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(TQueryParameter param)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
                           throw new Exception("Failed to get Query service");
        return queryService
                   .ForMultiProjectionQueryAsync<TMultiProjectionPayload, TQuery, TQueryParameter,
                       TQueryResponse>(param).Result ??
               throw new Exception("Failed to get Multi Projection Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteMultiProjectionQueryResponseToFile<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Json is null or empty");
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionQueryResponseIs<TMultiProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var actual =
            GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(
            GetMultiProjectionQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param));
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionQueryResponseIsFromJson<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenMultiProjectionQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param,
            response);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionQueryResponseIsFromFile<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);

        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenMultiProjectionQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param,
            response);
        return this;
    }

    #endregion

    #region Multi Projection　List Query

    private ListQueryResult<TQueryResponse> GetMultiProjectionListQueryResponse<TMultiProjectionPayload, TQuery,
        TQueryParameter, TQueryResponse>(
        TQueryParameter param)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
                           throw new Exception("Failed to get Query service");
        return queryService
                   .ForMultiProjectionListQueryAsync<TMultiProjectionPayload, TQuery, TQueryParameter,
                       TQueryResponse>(param).Result ??
               throw new Exception("Failed to get Multi Projection List Query Response for " + typeof(TQuery).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteMultiProjectionListQueryResponseToFile<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var json = SekibanJsonHelper.Serialize(
            GetMultiProjectionListQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        if (string.IsNullOrEmpty(json)) throw new InvalidDataException("Json is null or empty");
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionListQueryResponseIs<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
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

    public UnifiedTestBase<TDependencyDefinition> ThenGetMultiProjectionListQueryResponse<TMultiProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        responseAction(
            GetMultiProjectionListQueryResponse<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param));
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionListQueryResponseIsFromJson<
        TMultiProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenMultiProjectionListQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param,
            response);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionListQueryResponseIsFromFile<
        TMultiProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TMultiProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        ThenMultiProjectionListQueryResponseIs<TMultiProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(param,
            response);
        return this;
    }

    #endregion

    #region Multi Projection

    public MultiProjectionState<TMultiProjectionPayload> GetMultiProjectionState<TMultiProjectionPayload>()
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
                                     throw new Exception("Failed to get Query service");
        return multiProjectionService.GetMultiProjectionAsync<TMultiProjectionPayload>().Result ??
               throw new Exception(
                   "Failed to get Multi Projection Response for " + typeof(TMultiProjectionPayload).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionPayloadIsFromFile<TMultiProjectionPayload>(
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<TMultiProjectionPayload>(openStream);
        if (projection is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        return ThenMultiProjectionPayloadIs(projection);
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetMultiProjectionPayload<TMultiProjectionPayload>(
        Action<TMultiProjectionPayload> payloadAction)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
    {
        payloadAction(GetMultiProjectionState<TMultiProjectionPayload>().Payload);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetMultiProjectionState<TMultiProjectionPayload>(
        Action<MultiProjectionState<TMultiProjectionPayload>> stateAction)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
    {
        stateAction(GetMultiProjectionState<TMultiProjectionPayload>());
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionStateIs<TMultiProjectionPayload>(
        MultiProjectionState<TMultiProjectionPayload> state)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
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

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionPayloadIs<TMultiProjectionPayload>(
        TMultiProjectionPayload payload)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
    {
        var actual = GetMultiProjectionState<TMultiProjectionPayload>().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenMultiProjectionStateIsFromFile<TMultiProjectionPayload>(
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<MultiProjectionState<TMultiProjectionPayload>>(openStream);
        if (projection is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        return ThenMultiProjectionStateIs(projection);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteMultiProjectionStateToFile<TMultiProjectionPayload>(
        string filename)
        where TMultiProjectionPayload : IMultiProjectionPayload, new()
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
               throw new Exception("Failed to get Aggregate List Projection Response for " +
                                   typeof(TAggregatePayload).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateListProjectionPayloadIsFromFile<TAggregatePayload>(
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer.Deserialize<SingleProjectionListState<AggregateState<TAggregatePayload>>>(openStream);
        if (projection is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        return ThenAggregateListProjectionPayloadIs(projection);
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateListProjectionPayload<TAggregatePayload>(
        Action<SingleProjectionListState<AggregateState<TAggregatePayload>>> payloadAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        payloadAction(GetAggregateListProjectionState<TAggregatePayload>().Payload);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetAggregateListProjectionState<TAggregatePayload>(
        Action<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> stateAction)
        where TAggregatePayload : IAggregatePayload, new()
    {
        stateAction(GetAggregateListProjectionState<TAggregatePayload>());
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateListProjectionStateIs<TAggregatePayload>(
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

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateListProjectionPayloadIs<TAggregatePayload>(
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

    public UnifiedTestBase<TDependencyDefinition> ThenAggregateListProjectionStateIsFromFile<TAggregatePayload>(
        string filename)
        where TAggregatePayload : IAggregatePayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer
                .Deserialize<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>(
                    openStream);
        if (projection is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        return ThenAggregateListProjectionStateIs(projection);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteAggregateListProjectionStateToFile<TAggregatePayload>(
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
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
                                     throw new Exception("Failed to get Query service");
        return multiProjectionService.GetSingleProjectionListObject<TSingleProjectionPayload>().Result ??
               throw new Exception("Failed to get Single Projection List Projection Response for " +
                                   typeof(TSingleProjectionPayload).Name);
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionListPayloadIsFromFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer.Deserialize<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(
                openStream);
        if (projection is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        return ThenSingleProjectionListPayloadIs(projection);
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetSingleProjectionListPayload<TSingleProjectionPayload>(
        Action<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> payloadAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        payloadAction(GetSingleProjectionListState<TSingleProjectionPayload>().Payload);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenGetSingleProjectionListState<TSingleProjectionPayload>(
        Action<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
            stateAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        stateAction(GetSingleProjectionListState<TSingleProjectionPayload>());
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionListStateIs<TSingleProjectionPayload>(
        MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> state)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
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

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionListPayloadIs<TSingleProjectionPayload>(
        SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>> payload)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var actual = GetSingleProjectionListState<TSingleProjectionPayload>().Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> ThenSingleProjectionListStateIsFromFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer
                .Deserialize<
                    MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>(
                    openStream);
        if (projection is null) throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        return ThenSingleProjectionListStateIs(projection);
    }

    public UnifiedTestBase<TDependencyDefinition> WriteSingleProjectionListStateToFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        var json = SekibanJsonHelper.Serialize(GetSingleProjectionListState<TSingleProjectionPayload>());
        File.WriteAllTextAsync(filename, json);
        return this;
    }

    #endregion

    #region Given

    public UnifiedTestBase<TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }

    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(
        Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader;
        if (singleProjectionService is null) throw new Exception("Failed to get single aggregate service");
        var aggregate = singleProjectionService.AsDefaultStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ??
               throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }

    public IReadOnlyCollection<IEvent> GetLatestEvents()
    {
        return _commandExecutor.LatestEvents;
    }

    public IReadOnlyCollection<IEvent> GetAllAggregateEvents<TAggregatePayload>(Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new()
    {
        return _commandExecutor.GetAllAggregateEvents<TAggregatePayload>(aggregateId);
    }

    #region GivenEvents

    public UnifiedTestBase<TDependencyDefinition> GivenEvents(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEvents(events);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsWithPublish(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEventsWithPublish(events);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEvents(params IEvent[] events)
    {
        return GivenEvents(events.AsEnumerable());
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsWithPublish(params IEvent[] events)
    {
        return GivenEventsWithPublish(events.AsEnumerable());
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsFromJson(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJson(jsonEvents);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsFromJsonWithPublish(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJsonWithPublish(jsonEvents);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEvents(eventTouples);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEventsWithPublish(eventTouples);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEvents(eventTouples);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, IEventPayloadCommon payload)[] eventTouples)
    {
        _eventHandler.GivenEventsWithPublish(eventTouples);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsFromFile(string filename)
    {
        _eventHandler.GivenEventsFromFile(filename);
        return this;
    }

    public UnifiedTestBase<TDependencyDefinition> GivenEventsFromFileWithPublish(string filename)
    {
        _eventHandler.GivenEventsFromFileWithPublish(filename);
        return this;
    }

    #endregion


    #region Run Commands

    public Guid RunCommand<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new()
    {
        return _commandExecutor.ExecuteCommand(command, injectingAggregateId);
    }

    public Guid RunCommandWithPublish<TAggregatePayload>(ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new()
    {
        return _commandExecutor.ExecuteCommandWithPublish(command, injectingAggregateId);
    }

    public UnifiedTestBase<TDependencyDefinition> GivenCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }

    #endregion

    #endregion
}
