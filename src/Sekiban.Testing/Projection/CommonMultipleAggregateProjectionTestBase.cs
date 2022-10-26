using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
using Sekiban.Testing.Command;
using Sekiban.Testing.QueryFilter;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Projection;

public abstract class CommonMultipleAggregateProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition> : IDisposable,
    IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload>, ITestHelperEventSubscriber
    where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
    where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly AggregateTestCommandExecutor _commandExecutor;
    protected readonly List<IQueryFilterChecker<MultipleAggregateProjectionState<TProjectionPayload>>> _queryFilterCheckers = new();
    protected IServiceProvider _serviceProvider;

    public CommonMultipleAggregateProjectionTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueryFiltersFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new AggregateTestCommandExecutor(_serviceProvider);
    }
    public CommonMultipleAggregateProjectionTestBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _commandExecutor = new AggregateTestCommandExecutor(_serviceProvider);
    }
    public MultipleAggregateProjectionState<TProjectionPayload> State { get; protected set; }
        = new(new TProjectionPayload(), Guid.Empty, string.Empty, 0, 0);
    protected Exception? _latestException { get; set; }
    public Action<IAggregateEvent> OnEvent => e => GivenEvents(new List<IAggregateEvent> { e });

    public void Dispose()
    {
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenPayloadIsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<TProjectionPayload>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenPayloadIs(projection);
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenGetPayload(Action<TProjectionPayload> payloadAction)
    {
        payloadAction(State.Payload);
        return this;
    }

    public Guid RunCreateCommand<TAggregatePayload>(ICreateAggregateCommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        return aggregateId;
    }
    public void RunChangeCommand<TAggregatePayload>(ChangeAggregateCommandBase<TAggregatePayload> command)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var events = _commandExecutor.ExecuteChangeCommand(command);

    }

    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleAggregateService = _serviceProvider.GetRequiredService(typeof(ISingleAggregateService)) as ISingleAggregateService;
        if (singleAggregateService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleAggregateService.GetAggregateStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }
    public IReadOnlyCollection<IAggregateEvent> GetLatestEvents()
    {
        return _commandExecutor.LatestEvents;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(IEnumerable<IAggregateEvent> events)
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
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(params IAggregateEvent[] events)
    {
        return GivenEvents(events.AsEnumerable());
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEventsFromJson(string jsonEvents)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(jsonEvents);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public abstract IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> WhenProjection();

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenGetState(
        Action<MultipleAggregateProjectionState<TProjectionPayload>> stateAction)
    {
        stateAction(State);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenStateIs(
        MultipleAggregateProjectionState<TProjectionPayload> state)
    {
        var actual = State;
        var expected = state with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenPayloadIs(TProjectionPayload payload)
    {
        var actual = State.Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenStateIsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<MultipleAggregateProjectionState<TProjectionPayload>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenStateIs(projection);
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> WriteProjectionToFile(string filename)
    {
        var json = SekibanJsonHelper.Serialize(State);
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEventsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenQueryFilterChecker(
        IQueryFilterChecker<MultipleAggregateProjectionState<TProjectionPayload>> checker)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        checker.QueryFilterHandler = _serviceProvider.GetService<QueryFilterHandler>();
        _queryFilterCheckers.Add(checker);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples)
    {
        foreach (var (aggregateId, aggregateType, payload) in eventTouples)
        {
            var type = payload.GetType();
            var isCreateEvent = payload is ICreatedEventPayload;
            var eventType = typeof(AggregateEvent<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload, isCreateEvent) as IAggregateEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(ev);
        }
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(
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
            var eventType = typeof(AggregateEvent<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload, isCreateEvent) as IAggregateEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(ev);
        }
        return this;
    }

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenCommandExecutorAction(
        Action<AggregateTestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }
    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload>> GivenEventsFromFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var list = await JsonSerializer.DeserializeAsync<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload>> ThenStateFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var projection = await JsonSerializer.DeserializeAsync<MultipleAggregateProjectionState<TProjectionPayload>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenStateIs(projection);
    }

    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload>> WriteProjectionToFileAsync(string filename)
    {
        var json = SekibanJsonHelper.Serialize(State);
        await File.WriteAllTextAsync(filename, json);
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
            var eventType = typeof(AggregateEvent<>).MakeGenericType(eventPayloadType);
            if (eventType is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} の生成に失敗しました。");
            }
            var eventInstance = JsonSerializer.Deserialize(json.ToString(), eventType);
            if (eventInstance is null)
            {
                throw new InvalidDataException($"イベント {documentTypeName} のデシリアライズに失敗しました。");
            }
            var ev = eventInstance as IAggregateEvent;
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
