using MediatR;
using Sekiban.Core.Events;
namespace Sekiban.Core.PubSub;

/// <summary>
///     Event publisher using MediatR
/// </summary>
public class EventPublisher
{
    private readonly IMediator _mediator;

    public EventPublisher(IMediator mediator) => _mediator = mediator;

    public async Task PublishAsync<TEvent>(TEvent ev) where TEvent : IEvent
    {
        await _mediator.Publish(ev, CancellationToken.None);
    }
    public async Task PublishEventsAsync<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        foreach (var ev in events)
        {
            await PublishAsync(ev);
        }
    }
}
