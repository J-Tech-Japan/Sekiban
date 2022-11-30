using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Types;
using System.Text.Json;
namespace Sekiban.Testing.Projection;

public class TestEventHandler
{
    protected readonly IServiceProvider _serviceProvider;
    public TestEventHandler(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public void GivenEvents(IEnumerable<IEvent> events) => GivenEvents(events, false);
    public void GivenEventsWithPublish(IEnumerable<IEvent> events) => GivenEvents(events, true);
    private void GivenEvents(IEnumerable<IEvent> events, bool withPublish)
    {
        var documentWriter = _serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter;
        if (documentWriter is null) { throw new Exception("Failed to get document writer"); }
        var sekibanAggregateTypes = _serviceProvider.GetService<SekibanAggregateTypes>() ??
            throw new Exception("Failed to get aggregate types");

        foreach (var e in events)
        {
            var aggregateType = sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == e.AggregateType);
            if (aggregateType is null) { throw new Exception($"Failed to find aggregate type {e.AggregateType}"); }
            documentWriter.SaveAsync(e, aggregateType.Aggregate).Wait();
        }
    }
    public void GivenEvents(params IEvent[] events) => GivenEvents(events.AsEnumerable(), false);
    public void GivenEventsWithPublish(params IEvent[] events) => GivenEvents(events.AsEnumerable(), true);
    public void GivenEventsFromJson(string jsonEvents) => GivenEventsFromJson(jsonEvents, false);
    public void GivenEventsFromJsonWithPublish(string jsonEvents) => GivenEventsFromJson(jsonEvents, true);
    private void GivenEventsFromJson(string jsonEvents, bool withPublish)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(jsonEvents);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list, withPublish);
    }

    public void GivenEventsFromFile(string filename) => GivenEventsFromFile(filename, false);
    public void GivenEventsFromFileWithPublish(string filename) => GivenEventsFromFile(filename, true);
    private void GivenEventsFromFile(string filename, bool withPublish)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream);
        if (list is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        AddEventsFromList(list, withPublish);
    }
    public void GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTouples) => GivenEvents(false, eventTouples);
    public void GivenEventsWithPublish(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTouples) => GivenEvents(true, eventTouples);
    private void GivenEvents(
        bool withPublish,
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTouples)
    {
        foreach (var (aggregateId, aggregateType, payload) in eventTouples)
        {
            var type = payload.GetType();
            var eventType = typeof(Event<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload) as IEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(new[] { ev }, withPublish);
        }
    }
    public void GivenEvents(
        params (Guid aggregateId, IEventPayloadCommon payload)[] eventTouples) => GivenEvents(false, eventTouples);
    public void GivenEventsWithPublish(
        params (Guid aggregateId, IEventPayloadCommon payload)[] eventTouples) => GivenEvents(true, eventTouples);
    private void GivenEvents(
        bool withPublish,
        params (Guid aggregateId, IEventPayloadCommon payload)[] eventTouples)
    {
        foreach (var (aggregateId, payload) in eventTouples)
        {
            var type = payload.GetType();
            var aggregateType = payload.GetAggregatePayloadType();
            var eventType = typeof(Event<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload) as IEvent;
            if (ev == null) { throw new InvalidDataException("イベントの生成に失敗しました。" + payload); }
            GivenEvents(new[] { ev }, withPublish);
        }
    }
    private void AddEventsFromList(List<JsonElement> list, bool withPublish)
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
            GivenEvents(
                new List<IEvent>
                    { ev },
                withPublish);
        }
    }
}
