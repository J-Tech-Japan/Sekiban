using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Shared;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

public class CommonMultipleAggregateProjectionTestBase<TProjection> : IDisposable, IMultipleAggregateProjectionTestHelper<TProjection>
    where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
{
    protected readonly IServiceProvider _serviceProvider;
    protected List<IAggregateEvent> Events { get; } = new();
    protected TProjection Projection { get; } = new();
    protected Exception? _latestException { get; set; }

    protected CommonMultipleAggregateProjectionTestBase(SekibanDependencyOptions dependencyOptions)
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
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(IEnumerable<IAggregateEvent> events)
    {
        Events.AddRange(events);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(params IAggregateEvent[] events)
    {
        // ReSharper disable once TailRecursiveCall
        return GivenEvents(events);
    }
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(string jsonEvents)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(jsonEvents);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public async Task<IMultipleAggregateProjectionTestHelper<TProjection>> GivenEventsFromFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var list = await JsonSerializer.DeserializeAsync<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection> WhenProjection()
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
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }
    public IMultipleAggregateProjectionTestHelper<TProjection> ThenDto(TProjection dto)
    {
        var actual = Projection;
        var expected = dto;
        expected.LastEventId = actual.LastEventId;
        expected.LastSortableUniqueId = actual.LastSortableUniqueId;
        expected.Version = actual.Version;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }

    public async Task<IMultipleAggregateProjectionTestHelper<TProjection>> ThenDtoFileAsync(string filename)
    {
        await using var openStream = File.OpenRead(filename);
        var projection = await JsonSerializer.DeserializeAsync<TProjection>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenDto(projection);
    }

    public async Task<IMultipleAggregateProjectionTestHelper<TProjection>> WriteProjectionToFileAsync(string filename)
    {
        var json = SekibanJsonHelper.Serialize(Projection);
        await File.WriteAllTextAsync(filename, json);
        return this;
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
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(
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
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(params (Guid aggregateId, IEventPayload payload)[] eventTouples)
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
    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {

    }

    private void ResetBeforeCommand()
    {
        _latestException = null;
    }
}
