using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class EventPublishOnlyChangeAggregateCommandHandlerBase<T, C> : IChangeAggregateCommandHandler<T, C> where T : IAggregatePayload, new()
    where C : ChangeAggregateCommandBase<T>, IOnlyPublishingCommand, new()
{
    private List<IAggregateEvent> Events { get; } = new();
    protected Guid AggregateId { get; set; } = Guid.Empty;

    public Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, Aggregate<T> aggregate)
    {
        throw new SekibanCanNotExecuteRegularChangeCommandException(typeof(C).Name);
    }
    public async Task<AggregateCommandResponse> HandleForOnlyPublishingCommandAsync(
        AggregateCommandDocument<C> aggregateCommandDocument,
        Guid aggregateId)
    {
        AggregateId = aggregateId;
        var events = await ExecCommandAsync(aggregateId, aggregateCommandDocument.Payload);
        events.ToList().ForEach(ev => SaveEvent(ev));
        await Task.CompletedTask;
        return new AggregateCommandResponse(aggregateId, Events.ToImmutableList(), 0);
    }
    public virtual C CleanupCommandIfNeeded(C command)
    {
        return command;
    }
    protected abstract Task<IEnumerable<IChangedAggregateEventPayload<T>>> ExecCommandAsync(Guid aggregateId, C command);

    protected void SaveEvent<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedAggregateEventPayload<T>
    {
        var eventDocument = new AggregateEvent<TEventPayload>(AggregateId, typeof(T), payload);
        Events.Add(eventDocument);
    }
}
