using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace DaprSample.Api;

// Simple in-memory implementations for testing
public class InMemoryEventWriter : IEventWriter
{
    private readonly List<IEvent> _events = new();
    
    public Task SaveEvents(IReadOnlyList<IEvent> events)
    {
        _events.AddRange(events);
        return Task.CompletedTask;
    }
    
    public Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        _events.AddRange(events.Cast<IEvent>());
        return Task.CompletedTask;
    }
}

public class InMemoryEventReader : IEventReader
{
    public Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo retrievalInfo)
    {
        // Return empty list for now
        return Task.FromResult(ResultBox<IReadOnlyList<IEvent>>.FromValue(new List<IEvent>()));
    }
}