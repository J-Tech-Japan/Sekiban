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
        // Deserialize SerializableEvent to Event
        var eventResult = item.ToEvent(_domainTypes.EventTypes);
        if (!eventResult.IsSuccess)
        {
            return;
        }

        var evt = eventResult.GetValue();

        // Apply filter if configured
        if (_filter != null && !_filter.ShouldInclude(evt))
        {
            return;
        }

        await _onEvent(evt);
    }

    public Task OnCompletedAsync()
    {
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        return Task.CompletedTask;
    }
}
