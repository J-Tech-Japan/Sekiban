using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class EventPublishOnlyChangeAggregateCommandHandlerBase<T, C> : IChangeAggregateCommandHandler<T, C>
    where T : IAggregatePayload, new()
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
        var eventPayloads = ExecCommandAsync(aggregateId, aggregateCommandDocument.Payload);
        var events = new List<IAggregateEvent>();
        await foreach (var eventPayload in eventPayloads)
        {
            events.Add(SaveEvent(eventPayload));
        }
        await Task.CompletedTask;
        return new AggregateCommandResponse(aggregateId, events.ToImmutableList(), 0);
    }
    public virtual C CleanupCommandIfNeeded(C command)
    {
        return command;
    }
    protected abstract IAsyncEnumerable<IChangedAggregateEventPayload<T>> ExecCommandAsync(Guid aggregateId, C command);

    private IAggregateEvent SaveEvent<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedAggregateEventPayload<T>
    {
        return new AggregateEvent<TEventPayload>(AggregateId, typeof(T), payload);
    }
}
