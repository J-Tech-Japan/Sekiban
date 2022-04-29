using MediatR;
using Sekiban.EventSourcing.AggregateEvents;

namespace Sekiban.EventSourcing.PubSubs;

public class AggregateEventPublisher
{
    private readonly IMediator _mediator;

    public AggregateEventPublisher(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task PublishAsync<TEvent>(TEvent ev)
        where TEvent : AggregateEvent
    {
        await _mediator.Publish(ev, CancellationToken.None);
    }
}
