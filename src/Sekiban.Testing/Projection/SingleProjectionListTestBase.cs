using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Testing.Command;
using Sekiban.Testing.Queries;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Projection;

public class
    SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> : ITestHelperEventSubscriber
    where TAggregatePayload : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    protected readonly List<IQueryChecker>
        _queryCheckers = new();
    protected IServiceProvider _serviceProvider;

    public SingleProjectionListTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
    }
    public SingleProjectionListTestBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
    }



    public MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> State { get; protected set; }
        = new(new SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>(), Guid.Empty, string.Empty, 0, 0);
    protected Exception? _latestException { get; set; }
    public Action<IEvent> OnEvent => e => GivenEvents(new List<IEvent> { e });




    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
            TDependencyDefinition>
        WhenProjection()
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider not set");
        }
        var multipleProjectionService
            = _serviceProvider.GetRequiredService(typeof(IMultiProjectionService)) as IMultiProjectionService;
        if (multipleProjectionService is null) { throw new Exception("Failed to get multipleProjectionService "); }
        try
        {
            State = multipleProjectionService
                .GetSingleProjectionListObject<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>()
                .Result;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        return this;
    }

    public void Dispose()
    {
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenPayloadIsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenPayloadIs(projection);
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetPayload(Action<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> payloadAction)
    {
        payloadAction(State.Payload);
        return this;
    }

    public Guid RunCreateCommand<TCommandAggregatePayload>(ICreateCommand<TCommandAggregatePayload> command, Guid? injectingAggregateId = null)
        where TCommandAggregatePayload : IAggregatePayload, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        return aggregateId;
    }
    public void RunChangeCommand<TCommandAggregatePayload>(ChangeCommandBase<TCommandAggregatePayload> command)
        where TCommandAggregatePayload : IAggregatePayload, new()
    {
        var events = _commandExecutor.ExecuteChangeCommand(command);
    }

    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleProjectionService)) as ISingleProjectionService;
        if (singleAggregateService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleAggregateService.GetAggregateStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }
    public IReadOnlyCollection<IEvent> GetLatestEvents() => _commandExecutor.LatestEvents;
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(IEnumerable<IEvent> events)
    {
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        var sekibanAggregateTypes = _serviceProvider.GetService<SekibanAggregateTypes>() ?? throw new Exception("Failed to get aggregate types");

        foreach (var e in events)
        {
            var aggregateType = sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == e.AggregateType);
            if (aggregateType is null) { throw new Exception($"Failed to find aggregate type {e.AggregateType}"); }
            documentWriter.SaveAsync(e, aggregateType.Aggregate).Wait();
        }
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(params IEvent[] events) =>
        GivenEvents(events.AsEnumerable());
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsFromJson(string jsonEvents)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(jsonEvents);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.NotNull(exception);
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetState(
        Action<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>> stateAction)
    {
        stateAction(State);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenStateIs(
        MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> state)
    {
        var actual = State;
        var expected = state with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenPayloadIs(SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>> payload)
    {
        var actual = State.Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenStateIsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer.Deserialize<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenStateIs(projection);
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> WriteProjectionToFile(string filename)
    {
        var json = SekibanJsonHelper.Serialize(State);
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenQueryChecker(
        IQueryChecker checker)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        checker.QueryService = _serviceProvider.GetService<IQueryService>();
        _queryCheckers.Add(checker);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples)
    {
        foreach (var (aggregateId, aggregateType, payload) in eventTouples)
        {
            var type = payload.GetType();
            var isCreateEvent = payload is ICreatedEventPayload;
            var eventType = typeof(Event<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload, isCreateEvent) as IEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(ev);
        }
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples)
    {
        foreach (var (aggregateId, payload) in eventTouples)
        {
            var type = payload.GetType();
            var isCreateEvent = payload is ICreatedEventPayload;
            var interfaces = payload.GetType().GetInterfaces();
            var interfaceType = payload.GetType()
                .GetInterfaces()
                ?.FirstOrDefault(m => m.IsGenericType && m.GetGenericTypeDefinition() == typeof(IApplicableEvent<>));
            var aggregateType = interfaceType?.GenericTypeArguments?.FirstOrDefault();
            if (aggregateType is null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            var eventType = typeof(Event<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload, isCreateEvent) as IEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(ev);
        }
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenCommandExecutorAction(
        Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetQueryTest<TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryChecker = new SingleProjectionQueryTestChecker<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetListQueryTest<TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryChecker = new SingleProjectionListQueryTestChecker<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryChecker(queryChecker);
        queryTestAction(queryChecker);
        return this;
    }
    public T GetService<T>() where T : notnull
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        return _serviceProvider.GetRequiredService<T>() ?? throw new Exception($"Service {typeof(T)} not found");
    }

    private void AddEventsFromList(List<JsonElement> list)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        var registeredEventTypes = _serviceProvider.GetService<RegisteredEventTypes>();
        if (registeredEventTypes is null) { throw new InvalidOperationException("RegisteredEventTypes が登録されていません。"); }
        foreach (var json in list)
        {
            var documentTypeName = json.GetProperty("DocumentTypeName").ToString();
            var eventPayloadType = registeredEventTypes.RegisteredTypes.FirstOrDefault(e => e.Name == documentTypeName);
            if (eventPayloadType is null)
            {
                throw new InvalidDataException($"イベントタイプ {documentTypeName} は登録されていません。");
            }
            var eventType = typeof(Event<>).MakeGenericType(eventPayloadType);
            if (eventType is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} の生成に失敗しました。");
            }
            var eventInstance = JsonSerializer.Deserialize(json.ToString(), eventType);
            if (eventInstance is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} のデシリアライズに失敗しました。");
            }
            var ev = eventInstance as IEvent;
            if (ev is null) { throw new InvalidDataException($"イベント {documentTypeName} の生成に失敗しました。"); }
            GivenEvents(ev);
        }
    }
    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {

    }

    private void ResetBeforeCommand()
    {
        _latestException = null;
    }
}
