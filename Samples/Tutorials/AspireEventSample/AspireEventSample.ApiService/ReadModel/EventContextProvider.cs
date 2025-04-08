using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.ReadModel;

/// <summary>
///     Implementation of event context provider
/// </summary>
public class EventContextProvider : IEventContextProvider
{
    private readonly AsyncLocal<EventContext> _currentEventContext = new();

    public EventContext GetCurrentEventContext()
    {
        var context = _currentEventContext.Value;
        if (context == null)
        {
            throw new InvalidOperationException("No event is being processed currently");
        }

        return context;
    }

    public void SetCurrentEventContext(IEvent @event)
    {
        _currentEventContext.Value = new EventContext(@event);
    }

    public void ClearCurrentEventContext()
    {
        _currentEventContext.Value = null!;
    }
}