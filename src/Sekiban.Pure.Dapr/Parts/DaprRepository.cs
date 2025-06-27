using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Handlers;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Parts;

/// <summary>
/// Repository implementation for Dapr actors, bridging between AggregateActor and AggregateEventHandlerActor.
/// This is the Dapr equivalent of Orleans' OrleansRepository.
/// </summary>
public class DaprRepository
{
    private readonly IAggregateEventHandlerActor _eventHandlerActor;
    private readonly PartitionKeys _partitionKeys;
    private readonly IAggregateProjector _projector;
    private readonly IEventTypes _eventTypes;
    private Aggregate _currentAggregate;

    public DaprRepository(
        IAggregateEventHandlerActor eventHandlerActor,
        PartitionKeys partitionKeys,
        IAggregateProjector projector,
        IEventTypes eventTypes,
        Aggregate currentAggregate)
    {
        _eventHandlerActor = eventHandlerActor ?? throw new ArgumentNullException(nameof(eventHandlerActor));
        _partitionKeys = partitionKeys ?? throw new ArgumentNullException(nameof(partitionKeys));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _currentAggregate = currentAggregate ?? throw new ArgumentNullException(nameof(currentAggregate));
    }

    public ResultBox<Aggregate> GetAggregate()
    {
        return ResultBox<Aggregate>.FromValue(_currentAggregate);
    }

    public async Task<ResultBox<List<IEvent>>> Save(
        string lastSortableUniqueId,
        List<IEvent> newEvents)
    {
        if (newEvents == null || newEvents.Count == 0)
        {
            return ResultBox<List<IEvent>>.FromValue(new List<IEvent>());
        }

        try
        {
            // Convert events to envelopes
            var eventEnvelopes = new List<EventEnvelope>();
            foreach (var @event in newEvents)
            {
                // Get the actual event payload to serialize and get its type
                var eventPayload = @event.GetPayload();
                var eventPayloadType = eventPayload.GetType();
                
                var envelope = new EventEnvelope
                {
                    EventType = eventPayloadType.AssemblyQualifiedName ?? eventPayloadType.FullName ?? eventPayloadType.Name,
                    EventPayload = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(eventPayload, eventPayloadType)),
                    AggregateId = _partitionKeys.AggregateId.ToString(),
                    PartitionId = _partitionKeys.AggregateId,
                    RootPartitionKey = _partitionKeys.RootPartitionKey,
                    Version = @event.Version,
                    SortableUniqueId = @event.GetSortableUniqueId(),
                    Timestamp = DateTime.UtcNow,
                    Metadata = new Dictionary<string, string>(),
                    CorrelationId = string.Empty,
                    CausationId = string.Empty
                };
                eventEnvelopes.Add(envelope);
            }
            
            // Call the event handler actor to append events
            var response = await _eventHandlerActor.AppendEventsAsync(
                lastSortableUniqueId,
                eventEnvelopes);
                
            if (!response.IsSuccess)
            {
                return ResultBox<List<IEvent>>.FromException(new InvalidOperationException(response.ErrorMessage));
            }

            // Update current aggregate with new events
            _currentAggregate = _currentAggregate.Project(newEvents, _projector).UnwrapBox();

            return ResultBox<List<IEvent>>.FromValue(newEvents);
        }
        catch (Exception ex)
        {
            return ResultBox<List<IEvent>>.FromException(ex);
        }
    }

    public async Task<ResultBox<Aggregate>> Load()
    {
        try
        {
            // Get all events from the event handler
            var eventEnvelopes = await _eventHandlerActor.GetAllEventsAsync();
            
            // Convert envelopes back to events
            var events = new List<IEvent>();
            foreach (var envelope in eventEnvelopes)
            {
                var eventType = Type.GetType(envelope.EventType);
                if (eventType != null)
                {
                    var eventJson = System.Text.Encoding.UTF8.GetString(envelope.EventPayload);
                    var @event = JsonSerializer.Deserialize(eventJson, eventType) as IEvent;
                    if (@event != null)
                    {
                        events.Add(@event);
                    }
                }
            }
            
            // Start with empty aggregate
            var aggregate = Aggregate.EmptyFromPartitionKeys(_partitionKeys);
            
            // Project all events
            aggregate = aggregate.Project(events, _projector).UnwrapBox();
            
            // Update current aggregate
            _currentAggregate = aggregate;
            
            return ResultBox<Aggregate>.FromValue(aggregate);
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
    }

    public ResultBox<Aggregate> GetProjectedAggregate(List<IEvent> projectedEvents)
    {
        try
        {
            var aggregate = _currentAggregate.Project(projectedEvents, _projector).UnwrapBox();
            return ResultBox<Aggregate>.FromValue(aggregate);
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
    }
}