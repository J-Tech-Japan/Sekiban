using MediatR;
using Sekiban.Core.Events;

namespace Sekiban.Core.PubSub;

public class EventPublisher
{
    private readonly IMediator _mediator;

    public EventPublisher(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task PublishAsync<TEvent>(TEvent ev) where TEvent : IEvent
    {
        await _mediator.Publish(ev, CancellationToken.None);
    }
}
