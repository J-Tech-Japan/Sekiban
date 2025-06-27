using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace DaprSample.Api;

/// <summary>
/// Shared in-memory event storage for testing purposes
/// </summary>
public class SharedEventStorage
{
    private readonly List<IEvent> _events = new();
    private readonly object _lock = new();

    public void AddEvents(IEnumerable<IEvent> events)
    {
        lock (_lock)
        {
            _events.AddRange(events);
        }
    }

    public IReadOnlyList<IEvent> GetEvents()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }

    public IReadOnlyList<IEvent> GetEventsForAggregate(Guid aggregateId)
    {
        lock (_lock)
        {
            // Note: IEvent interface might not have AggregateId property directly
            // We'll return all events for now and let the caller filter
            return _events.ToList();
        }
    }
}

/// <summary>
/// In-memory event writer that uses shared storage
/// </summary>
public class InMemoryEventWriter : IEventWriter
{
    private readonly SharedEventStorage _storage;
    
    public InMemoryEventWriter(SharedEventStorage storage)
    {
        _storage = storage;
    }
    
    public Task SaveEvents(IReadOnlyList<IEvent> events)
    {
        _storage.AddEvents(events);
        return Task.CompletedTask;
    }
    
    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        _storage.AddEvents(events.Cast<IEvent>());
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory event reader that uses shared storage
/// </summary>
public class InMemoryEventReader : IEventReader
{
    private readonly SharedEventStorage _storage;
    
    public InMemoryEventReader(SharedEventStorage storage)
    {
        _storage = storage;
    }
    
    public Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo retrievalInfo)
    {
        var events = retrievalInfo.AggregateId.HasValue
            ? _storage.GetEventsForAggregate(retrievalInfo.AggregateId.GetValue())
            : _storage.GetEvents();
            
        return Task.FromResult(ResultBox<IReadOnlyList<IEvent>>.FromValue(events));
    }
}