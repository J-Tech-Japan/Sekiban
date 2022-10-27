using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class
    EventPublishOnlyChangeAggregateCommandHandlerBase<TAggregatePayload, TCommand> : IChangeAggregateCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new()
    where TCommand : ChangeAggregateCommandBase<TAggregatePayload>, IOnlyPublishingCommand, new()
{
    private List<IAggregateEvent> Events { get; } = new();
    protected Guid AggregateId { get; set; } = Guid.Empty;

    public Task<AggregateCommandResponse> HandleAsync(
        AggregateCommandDocument<TCommand> aggregateCommandDocument,
        Aggregate<TAggregatePayload> aggregate)
    {
        throw new SekibanCanNotExecuteRegularChangeCommandException(typeof(TCommand).Name);
    }
    public async Task<AggregateCommandResponse> HandleForOnlyPublishingCommandAsync(
        AggregateCommandDocument<TCommand> aggregateCommandDocument,
        Guid aggregateId)
    {
        AggregateId = aggregateId;
        var eventPayloads = ExecCommandAsync(aggregateId, aggregateCommandDocument.Payload);
        var events = new List<IAggregateEvent>();
        await foreach (var eventPayload in eventPayloads)
        {
            events.Add(
                AggregateEventHandler.GenerateEventToSave<IChangedAggregateEventPayload<TAggregatePayload>, TAggregatePayload>(
                    aggregateId,
                    eventPayload));
        }
        await Task.CompletedTask;
        return new AggregateCommandResponse(aggregateId, events.ToImmutableList(), 0);
    }
    public virtual TCommand CleanupCommandIfNeeded(TCommand command)
    {
        return command;
    }
    protected abstract IAsyncEnumerable<IChangedAggregateEventPayload<TAggregatePayload>> ExecCommandAsync(Guid aggregateId, TCommand command);
}
