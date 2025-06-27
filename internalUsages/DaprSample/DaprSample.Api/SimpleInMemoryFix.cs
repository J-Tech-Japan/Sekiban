using Dapr.Actors;
using Dapr.Actors.Runtime;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Parts;
using Sekiban.Pure.Events;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DaprSample.Api;

/// <summary>
/// Simple replacement for AggregateEventHandlerActor that returns empty results immediately
/// </summary>
public class SimpleAggregateEventHandlerActor : Actor, IAggregateEventHandlerActor
{
    private readonly ILogger<SimpleAggregateEventHandlerActor> _logger;
    private static readonly ConcurrentDictionary<string, List<EventEnvelope>> _eventStore = new();
    
    public SimpleAggregateEventHandlerActor(
        ActorHost host, 
        ILogger<SimpleAggregateEventHandlerActor> logger) : base(host)
    {
        _logger = logger;
    }

    protected override Task OnActivateAsync()
    {
        _logger.LogInformation("SimpleAggregateEventHandlerActor activated: {ActorId}", Id.GetId());
        return Task.CompletedTask;
    }

    public Task<EventHandlingResponse> AppendEventsAsync(string expectedLastSortableUniqueId, List<EventEnvelope> newEvents)
    {
        _logger.LogInformation("AppendEventsAsync called with {Count} events", newEvents.Count);
        
        // Store events in memory
        var actorId = Id.GetId();
        var events = _eventStore.GetOrAdd(actorId, new List<EventEnvelope>());
        lock (events)
        {
            events.AddRange(newEvents);
        }
        
        var lastId = newEvents.Any() ? newEvents.Last().SortableUniqueId : expectedLastSortableUniqueId;
        _logger.LogInformation("Stored {Count} events, total: {Total}", newEvents.Count, events.Count);
        
        return Task.FromResult(EventHandlingResponse.Success(lastId));
    }

    public Task<List<EventEnvelope>> GetAllEventsAsync()
    {
        _logger.LogInformation("GetAllEventsAsync called for {ActorId}", Id.GetId());
        
        var actorId = Id.GetId();
        if (_eventStore.TryGetValue(actorId, out var events))
        {
            _logger.LogInformation("Returning {Count} events", events.Count);
            return Task.FromResult(events.ToList());
        }
        
        _logger.LogInformation("No events found, returning empty list");
        return Task.FromResult(new List<EventEnvelope>());
    }

    public Task<List<EventEnvelope>> GetDeltaEventsAsync(string fromSortableUniqueId, int limit)
    {
        _logger.LogInformation("GetDeltaEventsAsync called from {FromId} with limit {Limit}", fromSortableUniqueId, limit);
        
        var allEvents = GetAllEventsAsync().Result;
        
        if (string.IsNullOrWhiteSpace(fromSortableUniqueId))
        {
            return Task.FromResult(limit > 0 ? allEvents.Take(limit).ToList() : allEvents);
        }

        var index = allEvents.FindIndex(e => e.SortableUniqueId == fromSortableUniqueId);
        if (index < 0)
        {
            return Task.FromResult(new List<EventEnvelope>());
        }

        var deltaEvents = allEvents.Skip(index + 1);
        return Task.FromResult(limit > 0 ? deltaEvents.Take(limit).ToList() : deltaEvents.ToList());
    }

    public Task<string> GetLastSortableUniqueIdAsync()
    {
        var events = GetAllEventsAsync().Result;
        return Task.FromResult(events.Any() ? events.Last().SortableUniqueId : string.Empty);
    }

    public Task RegisterProjectorAsync(string projectorKey)
    {
        _logger.LogInformation("RegisterProjectorAsync called with {ProjectorKey}", projectorKey);
        return Task.CompletedTask;
    }

}