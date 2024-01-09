using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Testing.Command;
using Sekiban.Testing.Projection;
using Sekiban.Testing.SingleProjections;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
namespace Sekiban.Testing;

/// <summary>
///     Unified test base class.
///     Inherit this class to create a test class.
/// </summary>
/// <typeparam name="TDependencyDefinition"></typeparam>
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
        var dependencyDefinition = new TDependencyDefinition();
        dependencyDefinition.Define();
        services.AddQueriesFromDependencyDefinition(dependencyDefinition);
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        var outputHelper = new TestOutputHelper();
        services.AddSingleton<ITestOutputHelper>(outputHelper);
        services.AddLogging(builder => builder.AddXUnit(outputHelper));
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
        _eventHandler = new TestEventHandler(_serviceProvider);
    }
    /// <summary>
    ///     Setup dependency for test.
    /// </summary>
    /// <param name="serviceCollection"></param>
    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {
    }

    #region Get Aggregate Test
    /// <summary>
    ///     Get aggregate test from
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public AggregateTest<TAggregatePayload, TDependencyDefinition> GetAggregateTest<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey) where TAggregatePayload : IAggregatePayloadCommon =>
        new(_serviceProvider, aggregateId, rootPartitionKey);
    /// <summary>
    ///     Get Aggregate test
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public AggregateTest<TAggregatePayload, TDependencyDefinition> GetAggregateTest<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayloadCommon =>
        new(_serviceProvider);
    /// <summary>
    ///     Get Aggregate test in action.
    ///     Root Partition Key will be default
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="testAction"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Guid aggregateId,
        Action<AggregateTest<TAggregatePayload, TDependencyDefinition>> testAction) where TAggregatePayload : IAggregatePayloadCommon =>
        ThenGetAggregateTest(aggregateId, IDocument.DefaultRootPartitionKey, testAction);
    /// <summary>
    ///     Get Aggregate test in action.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="testAction"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey,
        Action<AggregateTest<TAggregatePayload, TDependencyDefinition>> testAction) where TAggregatePayload : IAggregatePayloadCommon
    {
        testAction(new AggregateTest<TAggregatePayload, TDependencyDefinition>(_serviceProvider, aggregateId, rootPartitionKey));
        return this;
    }

    /// <summary>
    ///     Get Aggregate test in action.
    /// </summary>
    /// <param name="testAction"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetAggregateTest<TAggregatePayload>(
        Action<AggregateTest<TAggregatePayload, TDependencyDefinition>> testAction) where TAggregatePayload : IAggregatePayloadCommon
    {
        testAction(new AggregateTest<TAggregatePayload, TDependencyDefinition>(_serviceProvider));
        return this;
    }
    #endregion

    #region General List Query Test
    /// <summary>
    ///     Get Query Result
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private ListQueryResult<TQueryResponse> GetListQueryResponse<TQueryResponse>(IListQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ?? throw new SekibanTypeNotFoundException("Failed to get Query service");
        return queryService.ExecuteAsync(param).Result ??
            throw new SekibanTypeNotFoundException("Failed to get Aggregate Query Response for " + param.GetType().Name);
    }
    /// <summary>
    ///     Get Exception from Query
    ///     If query does not throw an exception, return null.
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private Exception? GetQueryException(IListQueryInputCommon param)
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ?? throw new SekibanTypeNotFoundException("Failed to get Query service");
        try
        {
            _ = queryService.ExecuteAsync((dynamic)param).Result;
        }
        catch (Exception e)
        {
            return e is AggregateException ae ? ae.InnerExceptions.First() : e;
        }
        return null;
    }
    /// <summary>
    ///     Check if query throws an exception of the specified type.
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryThrows<T>(IListQueryInputCommon param) where T : Exception
    {
        Assert.IsType<T>(GetQueryException(param));
        return this;
    }
    /// <summary>
    ///     Check if Query throws an exception of the specified type and check the exception.
    ///     and get the exception and can check the exception.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryGetException<T>(IListQueryInputCommon param, Action<T> checkException) where T : Exception
    {
        var exception = GetQueryException(param);
        Assert.NotNull(exception);
        Assert.IsType<T>(exception);
        checkException(exception as T ?? throw new SekibanTypeNotFoundException("Failed to cast exception"));
        return this;
    }
    /// <summary>
    ///     Run query and get exception and check exception.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryGetException<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<Exception> checkException) where TQueryResponse : IQueryResponse
    {
        var exception = GetQueryException(param);
        Assert.NotNull(exception);
        checkException(exception);
        return this;
    }
    /// <summary>
    ///     Run query and assume that no exception is thrown.
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryNotThrowsAnException(IListQueryInputCommon param)
    {
        Assert.Null(GetQueryException(param));
        return this;
    }
    /// <summary>
    ///     Run query and assume that an exception is thrown.
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryThrowsAnException(IListQueryInputCommon param)
    {
        Assert.NotNull(GetQueryException(param));
        return this;
    }
    /// <summary>
    ///     Run query and check the response.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="expectedResponse"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQueryResponse : IQueryResponse
    {
        var actual = GetListQueryResponse(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Run query and write query response to file.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> WriteQueryResponseToFile<TQueryResponse>(IListQueryInput<TQueryResponse> param, string filename)
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(GetListQueryResponse(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    /// <summary>
    ///     Run query and get query response in action
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseAction"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQueryResponse : IQueryResponse
    {
        responseAction(GetListQueryResponse(param));
        return this;
    }
    /// <summary>
    ///     Run query and check the response from json.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseJson"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromJson<TQueryResponse>(IListQueryInput<TQueryResponse> param, string responseJson)
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        ThenQueryResponseIs(param, response);
        return this;
    }
    /// <summary>
    ///     Run query and check the response from file.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseFilename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename) where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        ThenQueryResponseIs(param, response);
        return this;
    }
    #endregion

    #region Query Test (not list)
    /// <summary>
    ///     Get Query Response
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private TQueryResponse GetQueryResponse<TQueryResponse>(IQueryInput<TQueryResponse> param) where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ?? throw new SekibanTypeNotFoundException("Failed to get Query service");
        return queryService.ExecuteAsync(param).Result ??
            throw new SekibanTypeNotFoundException("Failed to get Aggregate Query Response for " + param.GetType().Name);
    }
    /// <summary>
    ///     Get Exception from Query, returns null if no exception is thrown.
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private Exception? GetQueryException(IQueryInputCommon param)
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ?? throw new SekibanTypeNotFoundException("Failed to get Query service");
        try
        {
            var _ = queryService.ExecuteAsync((dynamic)param).Result;
        }
        catch (Exception e)
        {
            return e is AggregateException ae ? ae.InnerExceptions.First() : e;
        }
        return null;
    }
    /// <summary>
    ///     Check if query throws an exception of the specified type.
    /// </summary>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryThrows<T>(IQueryInputCommon param) where T : Exception
    {
        Assert.IsType<T>(GetQueryException(param));
        return this;
    }
    /// <summary>
    ///     Check if Query throws an exception of the specified type and check the exception.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryGetException<T>(IQueryInputCommon param, Action<T> checkException) where T : Exception
    {
        var exception = GetQueryException(param);
        Assert.NotNull(exception);
        Assert.IsType<T>(exception);
        checkException(exception as T ?? throw new SekibanTypeNotFoundException("Failed to cast exception"));
        return this;
    }
    /// <summary>
    ///     Check if query throws an exception and get the exception with action to check.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="checkException"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryGetException(IQueryInputCommon param, Action<Exception> checkException)
    {
        var exception = GetQueryException(param);
        Assert.NotNull(exception);
        checkException(exception);
        return this;
    }
    /// <summary>
    ///     Check if query and assume that no exception is thrown.
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryNotThrowsAnException(IQueryInputCommon param)
    {
        Assert.Null(GetQueryException(param));
        return this;
    }
    /// <summary>
    ///     Runs query and assume that an exception is thrown.
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryThrowsAnException(IQueryInputCommon param)
    {
        Assert.NotNull(GetQueryException(param));
        return this;
    }

    /// <summary>
    ///     Runs query and check the response with the action
    /// </summary>
    /// <param name="param"></param>
    /// <param name="expectedResponse"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIs<TQueryResponse>(IQueryInput<TQueryResponse> param, TQueryResponse expectedResponse)
        where TQueryResponse : IQueryResponse
    {
        var actual = GetQueryResponse(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Runs query and write query response to file.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> WriteQueryResponseToFile<TQueryResponse>(IQueryInput<TQueryResponse> param, string filename)
        where TQueryResponse : IQueryResponse
    {
        var json = SekibanJsonHelper.Serialize(GetQueryResponse(param));
        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException("Json is null or empty");
        }
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    /// <summary>
    ///     Runs query and get query response in action
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseAction"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction) where TQueryResponse : IQueryResponse
    {
        responseAction(GetQueryResponse(param));
        return this;
    }
    /// <summary>
    ///     Runs query and check the response from json.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseJson"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromJson<TQueryResponse>(IQueryInput<TQueryResponse> param, string responseJson)
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson) ?? throw new InvalidDataException("Failed to serialize in JSON.");
        ThenQueryResponseIs(param, response);
        return this;
    }
    /// <summary>
    ///     Runs query and check the response from file.
    /// </summary>
    /// <param name="param"></param>
    /// <param name="responseFilename"></param>
    /// <typeparam name="TQueryResponse"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromFile<TQueryResponse>(IQueryInput<TQueryResponse> param, string responseFilename)
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream) ?? throw new InvalidDataException("Failed to serialize in JSON.");
        ThenQueryResponseIs(param, response);
        return this;
    }
    #endregion

    #region Multi Projection
    /// <summary>
    ///     Get Multi Projection State
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public MultiProjectionState<TMultiProjectionPayload> GetMultiProjectionState<TMultiProjectionPayload>(
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
            throw new SekibanTypeNotFoundException("Failed to get Query service");
        return multiProjectionService.GetMultiProjectionAsync<TMultiProjectionPayload>(rootPartitionKey, SortableUniqueIdValue.GetSafeIdFromUtc())
                .Result ??
            throw new SekibanTypeNotFoundException("Failed to get Multi Projection Response for " + typeof(TMultiProjectionPayload).Name);
    }
    /// <summary>
    ///     Check if Multi Projection Payload is same with file specified.
    /// </summary>
    /// <param name="filename"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionPayloadIsFromFile<TMultiProjectionPayload>(
        string filename,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<TMultiProjectionPayload>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        return ThenMultiProjectionPayloadIs(projection, rootPartitionKey);
    }
    /// <summary>
    ///     Get Multi Projection Payload in action.
    /// </summary>
    /// <param name="payloadAction"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetMultiProjectionPayload<TMultiProjectionPayload>(Action<TMultiProjectionPayload> payloadAction)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon =>
        ThenGetMultiProjectionPayload(IMultiProjectionService.ProjectionAllRootPartitions, payloadAction);

    public UnifiedTest<TDependencyDefinition> ThenGetMultiProjectionPayload<TMultiProjectionPayload>(
        string rootPartitionKey,
        Action<TMultiProjectionPayload> payloadAction) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        payloadAction(GetMultiProjectionState<TMultiProjectionPayload>(rootPartitionKey).Payload);
        return this;
    }
    /// <summary>
    ///     Get multi projection state in action.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="stateAction"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetMultiProjectionState<TMultiProjectionPayload>(
        string rootPartitionKey,
        Action<MultiProjectionState<TMultiProjectionPayload>> stateAction) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        stateAction(GetMultiProjectionState<TMultiProjectionPayload>(rootPartitionKey));
        return this;
    }
    /// <summary>
    ///     Check if multi projection state is same with specified state.
    /// </summary>
    /// <param name="state"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionStateIs<TMultiProjectionPayload>(MultiProjectionState<TMultiProjectionPayload> state)
        where TMultiProjectionPayload : IMultiProjectionPayloadCommon =>
        ThenMultiProjectionStateIs(IMultiProjectionService.ProjectionAllRootPartitions, state);
    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionStateIs<TMultiProjectionPayload>(
        string rootPartitionKey,
        MultiProjectionState<TMultiProjectionPayload> state) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        var actual = GetMultiProjectionState<TMultiProjectionPayload>(rootPartitionKey);
        var expected = state with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Check if multi projection payload is same with specified payload.
    /// </summary>
    /// <param name="payload"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionPayloadIs<TMultiProjectionPayload>(
        TMultiProjectionPayload payload,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        var actual = GetMultiProjectionState<TMultiProjectionPayload>(rootPartitionKey).Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Check if multi projection state is same with specified state from file.
    /// </summary>
    /// <param name="filename"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenMultiProjectionStateIsFromFile<TMultiProjectionPayload>(
        string filename,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<MultiProjectionState<TMultiProjectionPayload>>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        return ThenMultiProjectionStateIs(rootPartitionKey, projection);
    }
    /// <summary>
    ///     Write Multi Projection State to file.
    /// </summary>
    /// <param name="filename"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TMultiProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> WriteMultiProjectionStateToFile<TMultiProjectionPayload>(
        string filename,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TMultiProjectionPayload : IMultiProjectionPayloadCommon
    {
        var json = SekibanJsonHelper.Serialize(GetMultiProjectionState<TMultiProjectionPayload>(rootPartitionKey));
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    #endregion

    #region Aggregate List Projection
    /// <summary>
    ///     Get Aggregate List Projection State
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>> GetAggregateListProjectionState<TAggregatePayload>(
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TAggregatePayload : IAggregatePayloadCommon
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
            throw new SekibanTypeNotFoundException("Failed to get Query service");
        return multiProjectionService.GetAggregateListObject<TAggregatePayload>(rootPartitionKey, SortableUniqueIdValue.GetCurrentIdFromUtc())
                .Result ??
            throw new SekibanTypeNotFoundException("Failed to get Aggregate List Projection Response for " + typeof(TAggregatePayload).Name);
    }
    /// <summary>
    ///     Check if Aggregate List Projection Payload is same with file specified.
    /// </summary>
    /// <param name="filename"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionPayloadIsFromFile<TAggregatePayload>(
        string filename,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TAggregatePayload : IAggregatePayloadCommon
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<SingleProjectionListState<AggregateState<TAggregatePayload>>>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        return ThenAggregateListProjectionPayloadIs(rootPartitionKey, projection);
    }
    /// <summary>
    ///     Get Aggregate List Projection Payload in action.
    /// </summary>
    /// <param name="payloadAction"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetAggregateListProjectionPayload<TAggregatePayload>(
        Action<SingleProjectionListState<AggregateState<TAggregatePayload>>> payloadAction) where TAggregatePayload : IAggregatePayloadCommon =>
        ThenGetAggregateListProjectionPayload(IMultiProjectionService.ProjectionAllRootPartitions, payloadAction);

    /// <summary>
    ///     Get Aggregate List Projection Payload in action. This specifies the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="payloadAction"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetAggregateListProjectionPayload<TAggregatePayload>(
        string rootPartitionKey,
        Action<SingleProjectionListState<AggregateState<TAggregatePayload>>> payloadAction) where TAggregatePayload : IAggregatePayloadCommon
    {
        payloadAction(GetAggregateListProjectionState<TAggregatePayload>(rootPartitionKey).Payload);
        return this;
    }
    /// <summary>
    ///     Get Aggregate List Projection State in action.
    /// </summary>
    /// <param name="stateAction"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    // ReSharper disable once FunctionRecursiveOnAllPaths
    public UnifiedTest<TDependencyDefinition> ThenGetAggregateListProjectionState<TAggregatePayload>(
        Action<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> stateAction)
        where TAggregatePayload : IAggregatePayloadCommon =>
        ThenGetAggregateListProjectionState(stateAction);
    /// <summary>
    ///     Get Aggregate List Projection State in action. This specifies the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="stateAction"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetAggregateListProjectionState<TAggregatePayload>(
        string rootPartitionKey,
        Action<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> stateAction)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        stateAction(GetAggregateListProjectionState<TAggregatePayload>(rootPartitionKey));
        return this;
    }
    /// <summary>
    ///     Check if Aggregate List Projection State is same with specified state.
    /// </summary>
    /// <param name="state"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionStateIs<TAggregatePayload>(
        MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>> state) where TAggregatePayload : IAggregatePayloadCommon =>
        ThenAggregateListProjectionStateIs(IMultiProjectionService.ProjectionAllRootPartitions, state);
    /// <summary>
    ///     Check if Aggregate List Projection State is same with specified state. This specifies the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="state"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionStateIs<TAggregatePayload>(
        string rootPartitionKey,
        MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>> state) where TAggregatePayload : IAggregatePayloadCommon
    {
        var actual = GetAggregateListProjectionState<TAggregatePayload>(rootPartitionKey);
        var expected = state with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Check if Aggregate List Projection Payload is same with specified payload. This specifies the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="payload"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionPayloadIs<TAggregatePayload>(
        string rootPartitionKey,
        SingleProjectionListState<AggregateState<TAggregatePayload>> payload) where TAggregatePayload : IAggregatePayloadCommon
    {
        var actual = GetAggregateListProjectionState<TAggregatePayload>(rootPartitionKey).Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Check if Aggregate List Projection Payload is same with specified payload. This specifies the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenAggregateListProjectionStateIsFromFile<TAggregatePayload>(string rootPartitionKey, string filename)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        return ThenAggregateListProjectionStateIs(rootPartitionKey, projection);
    }
    /// <summary>
    ///     Write Aggregate List Projection State to file. This specifies the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> WriteAggregateListProjectionStateToFile<TAggregatePayload>(string rootPartitionKey, string filename)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var json = SekibanJsonHelper.Serialize(GetAggregateListProjectionState<TAggregatePayload>(rootPartitionKey));
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    #endregion

    #region Single Projection List Projection
    /// <summary>
    ///     Get Single Projection List State. Specify the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionListState<TSingleProjectionPayload>(string rootPartitionKey)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var multiProjectionService = _serviceProvider.GetService<IMultiProjectionService>() ??
            throw new SekibanTypeNotFoundException("Failed to get Query service");
        return multiProjectionService
                .GetSingleProjectionListObject<TSingleProjectionPayload>(rootPartitionKey, SortableUniqueIdValue.GetCurrentIdFromUtc())
                .Result ??
            throw new SekibanTypeNotFoundException(
                "Failed to get Single Projection List Projection Response for " + typeof(TSingleProjectionPayload).Name);
    }
    /// <summary>
    ///     Check if Single Projection List Payload is same with file specified. Specify the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListPayloadIsFromFile<TSingleProjectionPayload>(
        string rootPartitionKey,
        string filename) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        return ThenSingleProjectionListPayloadIs(rootPartitionKey, projection);
    }
    /// <summary>
    ///     Get Single Projection List Payload in action. No root partition key specified, and get all root partition key data.
    /// </summary>
    /// <param name="payloadAction"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetSingleProjectionListPayload<TSingleProjectionPayload>(
        Action<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> payloadAction)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        ThenGetSingleProjectionListPayload(IMultiProjectionService.ProjectionAllRootPartitions, payloadAction);

    public UnifiedTest<TDependencyDefinition> ThenGetSingleProjectionListPayload<TSingleProjectionPayload>(
        string rootPartitionKey,
        Action<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> payloadAction)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        payloadAction(GetSingleProjectionListState<TSingleProjectionPayload>(rootPartitionKey).Payload);
        return this;
    }
    /// <summary>
    ///     Get Single Projection List State in action. Specify the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="stateAction"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenGetSingleProjectionListState<TSingleProjectionPayload>(
        string rootPartitionKey,
        Action<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>> stateAction)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        stateAction(GetSingleProjectionListState<TSingleProjectionPayload>(rootPartitionKey));
        return this;
    }
    /// <summary>
    ///     Check if Single Projection List State is same with specified state. Specify the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="state"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListStateIs<TSingleProjectionPayload>(
        string rootPartitionKey,
        MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> state)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var actual = GetAggregateListProjectionState<TSingleProjectionPayload>(rootPartitionKey);
        var expected = state with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Check if Single Projection List Payload is same with specified payload. Specify the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="payload"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListPayloadIs<TSingleProjectionPayload>(
        string rootPartitionKey,
        SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>> payload)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var actual = GetSingleProjectionListState<TSingleProjectionPayload>(rootPartitionKey).Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    /// <summary>
    ///     Check if Single Projection List State is same with specified state from file. Specify the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidDataException"></exception>
    public UnifiedTest<TDependencyDefinition> ThenSingleProjectionListStateIsFromFile<TSingleProjectionPayload>(
        string rootPartitionKey,
        string filename) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        using var openStream = File.OpenRead(filename);
        var projection
            = JsonSerializer
                .Deserialize<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        return ThenSingleProjectionListStateIs(rootPartitionKey, projection);
    }
    /// <summary>
    ///     Write Single Projection List State to file. Specify the root partition key.
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="filename"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> WriteSingleProjectionListStateToFile<TSingleProjectionPayload>(string rootPartitionKey, string filename)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var json = SekibanJsonHelper.Serialize(GetSingleProjectionListState<TSingleProjectionPayload>(rootPartitionKey));
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    #endregion

    #region Given
    /// <summary>
    ///     Given action as scenario. Using this can test chain of commands.
    ///     Scenario is executing in separate with original test.
    /// </summary>
    /// <param name="initialAction"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    /// <summary>
    ///     Get current aggregate state.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TEnvironmentAggregatePayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="SekibanAggregateNotExistsException"></exception>
    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey) where TEnvironmentAggregatePayload : IAggregatePayloadCommon
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(IAggregateLoader)) as IAggregateLoader ??
            throw new SekibanTypeNotFoundException("Failed to get single aggregate service");
        var aggregate = singleProjectionService.AsDefaultStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name, rootPartitionKey);
    }
    /// <summary>
    ///     Get latest events from command.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyCollection<IEvent> GetLatestEvents() => _commandExecutor.LatestEvents;
    /// <summary>
    ///     Get all events for current aggregate.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public IReadOnlyCollection<IEvent> GetAllAggregateEvents<TAggregatePayload>(Guid aggregateId) where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.GetAllAggregateEvents<TAggregatePayload>(aggregateId);

    #region GivenEvents
    /// <summary>
    ///     Given events to prepare for test.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEvents(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEvents(events);
        return this;
    }
    /// <summary>
    ///     Given events and publish them. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEventsWithPublish(events);
        return this;
    }
    /// <summary>
    ///     Given events and publish them. Subscribed handlers will be executed and block until all handlers are executed.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublishAndBlockingSubscriptions(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEventsWithPublishAndBlockingSubscription(events);
        return this;
    }
    /// <summary>
    ///     Given events to prepare for test.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEvents(params IEvent[] events) => GivenEvents(events.AsEnumerable());
    /// <summary>
    ///     Given events and publish them. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(params IEvent[] events) => GivenEventsWithPublish(events.AsEnumerable());
    /// <summary>
    ///     Given events and publish them. Subscribed handlers will be executed and block until all handlers are executed.
    /// </summary>
    /// <param name="events"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublishAndBlockingSubscriptions(params IEvent[] events) =>
        GivenEventsWithPublishAndBlockingSubscriptions(events.AsEnumerable());
    /// <summary>
    ///     Given events from json string.
    /// </summary>
    /// <param name="jsonEvents"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsFromJson(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJson(jsonEvents);
        return this;
    }
    /// <summary>
    ///     Given events from json string and publish them. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="jsonEvents"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsFromJsonWithPublish(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJsonWithPublish(jsonEvents);
        return this;
    }
    /// <summary>
    ///     Given events from aggregate Id adn Payload
    /// </summary>
    /// <param name="eventTuples"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEvents(params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTuples)
    {
        _eventHandler.GivenEvents(eventTuples);
        return this;
    }
    /// <summary>
    ///     Given Events and publish them. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="eventTuples"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTuples)
    {
        _eventHandler.GivenEventsWithPublish(eventTuples);
        return this;
    }
    /// <summary>
    ///     Given events from aggregate Id, partition Key and Payload. Subscribed handlers will be executed and block until all
    ///     handlers are executed.
    /// </summary>
    /// <param name="eventTuples"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, string rootPartitionKey, IEventPayloadCommon payload)[] eventTuples)
    {
        _eventHandler.GivenEvents(eventTuples);
        return this;
    }
    /// <summary>
    ///     Given events from aggregate Id, partition Key and Payload. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="eventTuples"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, string rootPartitionKey, IEventPayloadCommon payload)[] eventTuples)
    {
        _eventHandler.GivenEventsWithPublish(eventTuples);
        return this;
    }
    /// <summary>
    ///     Given events from file.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsFromFile(string filename)
    {
        _eventHandler.GivenEventsFromFile(filename);
        return this;
    }
    /// <summary>
    ///     Given events from file and publish them. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenEventsFromFileWithPublish(string filename)
    {
        _eventHandler.GivenEventsFromFileWithPublish(filename);
        return this;
    }
    #endregion


    #region Run Commands
    /// <summary>
    ///     Run command and get aggregate Id.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunCommand<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommand(command, injectingAggregateId);
    /// <summary>
    ///     Run command as given condition and get aggregate Id.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid GivenCommand<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommand(command, injectingAggregateId);
    /// <summary>
    ///     Run command as when statement and get aggregate Id.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid WhenCommand<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommand(command, injectingAggregateId);
    /// <summary>
    ///     Run command and get aggregate Id. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunCommandWithPublish<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommandWithPublish(command, injectingAggregateId);
    /// <summary>
    ///     Run command as given condition and get aggregate Id. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid GivenCommandWithPublish<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommandWithPublish(command, injectingAggregateId);
    /// <summary>
    ///     Run command as when statement and get aggregate Id. Subscribed handlers will be executed.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid WhenCommandWithPublish<TAggregatePayload>(ICommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommandWithPublish(command, injectingAggregateId);
    /// <summary>
    ///     Run command and get aggregate Id. Subscribed handlers will be executed and block until all handlers are executed.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid RunCommandWithPublishAndBlockingSubscriptions<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null) where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommandWithPublishAndBlockingSubscriptions(command, injectingAggregateId);
    /// <summary>
    ///     Run command as given condition and get aggregate Id. Subscribed handlers will be executed and block until all
    ///     handlers are executed.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid GivenCommandWithPublishAndBlockingSubscriptions<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null) where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommandWithPublishAndBlockingSubscriptions(command, injectingAggregateId);
    /// <summary>
    ///     Run command as when statement and get aggregate Id. Subscribed handlers will be executed and block until all
    ///     handlers are executed.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="injectingAggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Guid WhenCommandWithPublishAndBlockingSubscriptions<TAggregatePayload>(
        ICommand<TAggregatePayload> command,
        Guid? injectingAggregateId = null) where TAggregatePayload : IAggregatePayloadCommon =>
        _commandExecutor.ExecuteCommandWithPublishAndBlockingSubscriptions(command, injectingAggregateId);
    /// <summary>
    ///     Given command executor action.
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public UnifiedTest<TDependencyDefinition> GivenCommandExecutorAction(Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }
    #endregion
    #endregion
}
