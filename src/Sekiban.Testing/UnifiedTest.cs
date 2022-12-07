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

    #region General List Query Test
    private ListQueryResult<TQueryResponse> GetListQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ExecuteAsync(param)
                .Result ??
            throw new Exception("Failed to get Aggregate Query Response for " + param.GetType().Name);
    }

    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TQueryResponse : IQueryResponse
    {
        var actual = GetListQueryResponse(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public UnifiedTest<TDependencyDefinition> WriteQueryResponseToFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string filename)
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
    public UnifiedTest<TDependencyDefinition> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TQueryResponse : IQueryResponse
    {
        responseAction(GetListQueryResponse(param)!);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromJson<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<ListQueryResult<TQueryResponse>>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
        return this;
    }
    #endregion

    #region Query Test (not list)
    private TQueryResponse GetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param)
        where TQueryResponse : IQueryResponse
    {
        var queryService = _serviceProvider.GetService<IQueryExecutor>() ??
            throw new Exception("Failed to get Query service");
        return queryService.ExecuteAsync(param)
                .Result ??
            throw new Exception("Failed to get Aggregate Query Response for " + param.GetType().Name);
    }

    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIs<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        TQueryResponse expectedResponse)
        where TQueryResponse : IQueryResponse
    {
        var actual = GetQueryResponse(param);
        var expected = expectedResponse;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public UnifiedTest<TDependencyDefinition> WriteQueryResponseToFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string filename)
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
    public UnifiedTest<TDependencyDefinition> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction)
        where TQueryResponse : IQueryResponse
    {
        responseAction(GetQueryResponse(param)!);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromJson<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse
    {
        var response = JsonSerializer.Deserialize<TQueryResponse>(responseJson);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
        return this;
    }

    public UnifiedTest<TDependencyDefinition> ThenQueryResponseIsFromFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse
    {
        using var openStream = File.OpenRead(responseFilename);
        var response = JsonSerializer.Deserialize<TQueryResponse>(openStream);
        if (response is null)
        {
            throw new InvalidDataException("JSON のでシリアライズに失敗しました。");
        }
        ThenQueryResponseIs(param, response);
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
