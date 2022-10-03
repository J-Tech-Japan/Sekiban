using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Shared;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

public class CommonMultipleAggregateProjectionTestBase<TProjection, TProjectionContents> : IDisposable,
    IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>
    where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
{
    private readonly List<IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>>> _queryFilterCheckers = new();
    protected readonly IServiceProvider _serviceProvider;
    protected List<IAggregateEvent> Events { get; } = new();
    protected TProjection Projection { get; } = new();
    protected Exception? _latestException { get; set; }

    public CommonMultipleAggregateProjectionTestBase(SekibanDependencyOptions dependencyOptions)
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        SekibanEventSourcingDependency.RegisterForAggregateTest(services, dependencyOptions);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(IEnumerable<IAggregateEvent> events)
    {
        Events.AddRange(events);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(params IAggregateEvent[] events)
    {
        return GivenEvents(events.AsEnumerable());
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(string jsonEvents)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(jsonEvents);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> GivenEventsFromFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var list = await JsonSerializer.DeserializeAsync<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WhenProjection()
    {
        ResetBeforeCommand();
        try
        {
            foreach (var ev in Events)
            {
                Projection.ApplyEvent(ev);
            }
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        var dto = Projection.ToDto();
        foreach (var checker in _queryFilterCheckers)
        {
            checker.RegisterDto(dto);
        }
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }

    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> ThenDtoFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var projection = await JsonSerializer.DeserializeAsync<MultipleAggregateProjectionContentsDto<TProjectionContents>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenDto(projection);
    }

    public async Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> WriteProjectionToFileAsync(string filename)
    {
        var json = SekibanJsonHelper.Serialize(Projection);
        await File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDto(
        MultipleAggregateProjectionContentsDto<TProjectionContents> dto)
    {
        var actual = Projection.ToDto();
        var expected = dto with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDtoFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<MultipleAggregateProjectionContentsDto<TProjectionContents>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenDto(projection);
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WriteProjectionToFile(string filename)
    {
        var json = SekibanJsonHelper.Serialize(Projection);
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEventsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenQueryFilterChecker(
        IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>> checker)
    {
        _queryFilterCheckers.Add(checker);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
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
            Events.Add(ev);
        }
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples)
    {
        foreach (var (aggregateId, payload) in eventTouples)
        {
            var type = payload.GetType();
            var isCreateEvent = payload is ICreatedEventPayload;
            var interfaces = payload.GetType().GetInterfaces();
            var interfaceType = payload.GetType()
                .GetInterfaces()
                ?.FirstOrDefault(m => m.IsGenericType && m.GetGenericTypeDefinition() == typeof(IAggregatePointerEvent<>));
            var aggregateType = interfaceType?.GenericTypeArguments?.FirstOrDefault();
            if (aggregateType is null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            var eventType = typeof(AggregateEvent<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload, isCreateEvent) as IAggregateEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            Events.Add(ev);
        }
        return this;
    }

    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }

    public T GetService<T>() where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>() ?? throw new Exception($"Service {typeof(T)} not found");
    }

    private void AddEventsFromList(List<JsonElement> list)
    {
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
            Events.Add((IAggregateEvent)eventInstance);
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
