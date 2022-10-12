namespace Sekiban.EventSourcing.AggregateCommands;

public abstract class EventPublishOnlyChangeAggregateCommandHandlerBase<T, C> : IChangeAggregateCommandHandler<T, C> where T : IAggregate
    where C : ChangeAggregateCommandBase<T>, IOnlyPublishingCommand, new()
{
    private List<IAggregateEvent> Events { get; } = new();
    protected Guid AggregateId { get; set; } = Guid.Empty;

    public Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, T aggregate)
    {
        throw new SekibanCanNotExecuteRegularChangeCommandException(typeof(C).Name);
    }
    public async Task<AggregateCommandResponse> HandleForOnlyPublishingCommandAsync(
        AggregateCommandDocument<C> aggregateCommandDocument,
        Guid aggregateId)
    {
        AggregateId = aggregateId;
        await ExecCommandAsync(aggregateId, aggregateCommandDocument.Payload);
        await Task.CompletedTask;
        return new AggregateCommandResponse(aggregateId, Events.AsReadOnly(), 0);
    }
    public virtual C CleanupCommandIfNeeded(C command)
    {
        return command;
    }
    protected abstract Task ExecCommandAsync(Guid aggregateId, C command);

    protected void SaveEvent<TEventPayload>(TEventPayload payload) where TEventPayload : IChangedAggregateEventPayload<T>
    {
        var eventDocument = new AggregateEvent<TEventPayload>(AggregateId, typeof(T), payload);
        Events.Add(eventDocument);
    }
}
