using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Observer for Orleans event streams
/// </summary>
public class EventStreamObserver : IAsyncObserver<SerializableEvent>
{
    private readonly IEventFilter? _filter;
    private readonly Func<Event, Task> _onEvent;
    private readonly string _subscriptionId;
    private readonly DcbDomainTypes _domainTypes;

    public EventStreamObserver(
        string subscriptionId,
        Func<Event, Task> onEvent,
        DcbDomainTypes domainTypes,
        IEventFilter? filter = null)
    {
        _subscriptionId = subscriptionId;
        _onEvent = onEvent;
        _domainTypes = domainTypes;
        _filter = filter;
    }

    public async Task OnNextAsync(SerializableEvent item, StreamSequenceToken? token = null)
    {
        Console.WriteLine($"[EventStreamObserver] Received event {item.EventPayloadName} for subscription {_subscriptionId}");

        // Deserialize SerializableEvent to Event
        var eventResult = item.ToEvent(_domainTypes.EventTypes);
        if (!eventResult.IsSuccess)
        {
            Console.WriteLine($"[EventStreamObserver] Failed to deserialize event: {eventResult.GetException().Message}");
            return;
        }

        var evt = eventResult.GetValue();

        // Apply filter if configured
        if (_filter != null && !_filter.ShouldInclude(evt))
        {
            Console.WriteLine($"[EventStreamObserver] Event filtered out for subscription {_subscriptionId}");
            return;
        }

        try
        {
            await _onEvent(evt);
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
