using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Observer for Orleans event streams
/// </summary>
public class EventStreamObserver : IAsyncObserver<Event>
{
    private readonly IEventFilter? _filter;
    private readonly Func<Event, Task> _onEvent;
    private readonly string _subscriptionId;

    public EventStreamObserver(string subscriptionId, Func<Event, Task> onEvent, IEventFilter? filter = null)
    {
        _subscriptionId = subscriptionId;
        _onEvent = onEvent;
        _filter = filter;
    }

    public async Task OnNextAsync(Event item, StreamSequenceToken? token = null)
    {
        Console.WriteLine($"[EventStreamObserver] Received event {item.EventType} for subscription {_subscriptionId}");

        // Apply filter if configured
        if (_filter != null && !_filter.ShouldInclude(item))
        {
            Console.WriteLine($"[EventStreamObserver] Event filtered out for subscription {_subscriptionId}");
            return;
        }

        try
        {
            await _onEvent(item);
            Console.WriteLine($"[EventStreamObserver] Event processed successfully for subscription {_subscriptionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EventStreamObserver] Error processing event: {ex.Message}");
        }
    }

    public Task OnCompletedAsync()
    {
        Console.WriteLine($"[EventStreamObserver] Stream completed for subscription {_subscriptionId}");
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        Console.WriteLine($"[EventStreamObserver] Stream error for subscription {_subscriptionId}: {ex.Message}");
        return Task.CompletedTask;
    }
}
