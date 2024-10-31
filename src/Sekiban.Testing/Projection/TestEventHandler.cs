using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.PubSub;
using Sekiban.Core.Types;
using System.Text.Json;
namespace Sekiban.Testing.Projection;

/// <summary>
///     Handle events for test.
///     Given events could be multiple aggregates
/// </summary>
/// <param name="serviceProvider"></param>
public class TestEventHandler(IServiceProvider serviceProvider)
{
    /// <summary>
    ///     Given events to test
    /// </summary>
    /// <param name="events"></param>
    public void GivenEvents(IEnumerable<IEvent> events)
    {
        GivenEvents(events, false);
    }
    /// <summary>
    ///     Given events to test and publish events
    /// </summary>
    /// <param name="events"></param>
    public void GivenEventsWithPublish(IEnumerable<IEvent> events)
    {
        GivenEvents(events, true);
    }
    /// <summary>
    ///     Given events to test and publish events
    ///     Even non blocking subscriptions will be executed by same thread and block the execution
    ///     (To test subscription values, use this method. But be careful, orders could be different from actual
    ///     execution)
    /// </summary>
    /// <param name="events"></param>
    /// <exception cref="Exception"></exception>
    public void GivenEventsWithPublishAndBlockingSubscription(IEnumerable<IEvent> events)
    {
        var nonBlockingStatus = serviceProvider.GetService<EventNonBlockingStatus>() ??
            throw new SekibanTypeNotFoundException("EventNonBlockingStatus could not be found");
        nonBlockingStatus.RunBlockingAction(() => GivenEvents(events, true));
    }

    private void GivenEvents(IEnumerable<IEvent> events, bool withPublish)
    {
        var documentWriter = serviceProvider.GetRequiredService(typeof(IDocumentWriter)) as IDocumentWriter ??
            throw new SekibanTypeNotFoundException("Failed to get document writer");
        var sekibanAggregateTypes = serviceProvider.GetService<SekibanAggregateTypes>() ??
            throw new SekibanTypeNotFoundException("Failed to get aggregate types");

        foreach (var e in events)
        {
            var aggregateType
                = sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == e.AggregateType) ??
                throw new SekibanTypeNotFoundException($"Failed to find aggregate type {e.AggregateType}");
            if (withPublish)
            {
                documentWriter.SaveAsync(e, new AggregateWriteStream(aggregateType.Aggregate)).Wait();
            } else
            {
                documentWriter
                    .SaveAndPublishEvents(new List<IEvent> { e }, new AggregateWriteStream(aggregateType.Aggregate))
                    .Wait();
            }
        }
    }
    /// <summary>
    ///     Given events to test
    /// </summary>
    /// <param name="events"></param>
    public void GivenEvents(params IEvent[] events)
    {
        GivenEvents(events.AsEnumerable(), false);
    }
    /// <summary>
    ///     Given events to test and publish events
    /// </summary>
    /// <param name="events"></param>
    public void GivenEventsWithPublish(params IEvent[] events)
    {
        GivenEvents(events.AsEnumerable(), true);
    }
    /// <summary>
    ///     Given events to test and publish events
    /// </summary>
    /// <param name="jsonEvents"></param>
    public void GivenEventsFromJson(string jsonEvents)
    {
        GivenEventsFromJson(jsonEvents, false);
    }
    /// <summary>
    ///     Given events to test and publish events
    /// </summary>
    /// <param name="jsonEvents"></param>
    public void GivenEventsFromJsonWithPublish(string jsonEvents)
    {
        GivenEventsFromJson(jsonEvents, true);
    }
    /// <summary>
    ///     Given events to test and publish events
    /// </summary>
    /// <param name="jsonEvents"></param>
    /// <param name="withPublish"></param>
    /// <exception cref="InvalidDataException"></exception>
    private void GivenEventsFromJson(string jsonEvents, bool withPublish)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(jsonEvents) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        AddEventsFromList(list, withPublish);
    }
    /// <summary>
    ///     Given events to test from File
    /// </summary>
    /// <param name="filename"></param>
    public void GivenEventsFromFile(string filename)
    {
        GivenEventsFromFile(filename, false);
    }
    /// <summary>
    ///     Given events to test from File
    ///     Also publish events
    /// </summary>
    /// <param name="filename"></param>
    public void GivenEventsFromFileWithPublish(string filename)
    {
        GivenEventsFromFile(filename, true);
    }

    private void GivenEventsFromFile(string filename, bool withPublish)
    {
        using var openStream = File.OpenRead(filename);
        var list = JsonSerializer.Deserialize<List<JsonElement>>(openStream) ??
            throw new InvalidDataException("Failed to serialize in JSON.");
        AddEventsFromList(list, withPublish);
    }
    /// <summary>
    ///     Given Events, by AggregateId, Type and Payload
    /// </summary>
    /// <param name="eventTuples"></param>
    public void GivenEvents(params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTuples)
    {
        GivenEvents(false, eventTuples);
    }
    /// <summary>
    ///     Given Events and publish events, by AggregateId, Type and Payload
    /// </summary>
    /// <param name="eventTuples"></param>
    public void GivenEventsWithPublish(
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTuples)
    {
        GivenEvents(true, eventTuples);
    }

    private void GivenEvents(
        bool withPublish,
        params (Guid aggregateId, Type aggregateType, IEventPayloadCommon payload)[] eventTuples)
    {
        foreach (var (aggregateId, aggregateType, payload) in eventTuples)
        {
            var type = payload.GetType();
            var eventType = typeof(Event<>);
            var genericType = eventType.MakeGenericType(type);
            var ev = Activator.CreateInstance(genericType, aggregateId, aggregateType, payload) as IEvent ??
                throw new InvalidDataException("Failed to generate an event" + payload);
            GivenEvents(new[] { ev }, withPublish);
        }
    }
    /// <summary>
    ///     Given Events, by AggregateId, Type and Payload
    /// </summary>
    /// <param name="eventTuples"></param>
    public void GivenEvents(
        params (Guid aggregateId, string rootPartitionKey, IEventPayloadCommon payload)[] eventTuples)
    {
        GivenEvents(false, eventTuples);
    }
    /// <summary>
    ///     Given Events and publish events, by AggregateId, Type and Payload
    /// </summary>
    /// <param name="eventTuples"></param>
    public void GivenEventsWithPublish(
        params (Guid aggregateId, string rootPartitionKey, IEventPayloadCommon payload)[] eventTuples)
    {
        GivenEvents(true, eventTuples);
    }

    private void GivenEvents(
        bool withPublish,
        params (Guid aggregateId, string rootPartitionKey, IEventPayloadCommon payload)[] eventTuples)
    {
        foreach (var (aggregateId, rootPartitionKey, payload) in eventTuples)
        {
            var type = payload.GetType();
            var aggregateType = payload.GetAggregatePayloadInType();
            var eventType = typeof(Event<>);
            var genericType = eventType.MakeGenericType(type);
            var ev
                = Activator.CreateInstance(
                    genericType,
                    aggregateId,
                    aggregateType,
                    payload,
                    rootPartitionKey) as IEvent ??
                throw new InvalidDataException("Failed to generate an event" + payload);
            GivenEvents(new[] { ev }, withPublish);
        }
    }

    private void AddEventsFromList(List<JsonElement> list, bool withPublish)
    {
        if (serviceProvider is null)
        {
            throw new SekibanTypeNotFoundException("Service provider is null. Please setup service provider.");
        }
        var registeredEventTypes = serviceProvider.GetService<RegisteredEventTypes>() ??
            throw new InvalidOperationException("RegisteredEventTypes is not registered.");
        foreach (var json in list)
        {
            var documentTypeName = json.GetProperty("DocumentTypeName").ToString();
            var eventPayloadType
                = registeredEventTypes.RegisteredTypes.FirstOrDefault(e => e.Name == documentTypeName) ??
                throw new InvalidDataException($"Event Type {documentTypeName} Is not registered.");
            var eventType = typeof(Event<>).MakeGenericType(eventPayloadType) ??
                throw new InvalidDataException($"Event {documentTypeName} failed to generate.");
            var eventInstance = JsonSerializer.Deserialize(json.ToString(), eventType) ??
                throw new InvalidDataException($"Event {documentTypeName} failed to deserialize.");
            var ev = eventInstance as IEvent ??
                throw new InvalidDataException($"Event {documentTypeName} failed to cast.");
            GivenEvents(new List<IEvent> { ev }, withPublish);
        }
    }
}
