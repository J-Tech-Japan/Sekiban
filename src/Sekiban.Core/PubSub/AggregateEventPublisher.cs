using MediatR;
using Sekiban.Core.Event;
namespace Sekiban.Core.PubSub;

public class AggregateEventPublisher
{
    private readonly IMediator _mediator;

    public AggregateEventPublisher(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task PublishAsync<TEvent>(TEvent ev) where TEvent : IAggregateEvent
    {
        await _mediator.Publish(ev, CancellationToken.None);
    }
}
