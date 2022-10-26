using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class CreateAggregateCommandHandlerBase<TAggregatePayload, TCommand> : ICreateAggregateCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ICreateAggregateCommand<TAggregatePayload>, new()
{

    public async Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<TCommand> command, Aggregate<TAggregatePayload> aggregate)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
        }
        var eventPayloads = ExecCreateCommandAsync(aggregate.ToState(), command.Payload);
        var events = new List<IAggregateEvent>();
        await foreach (var eventPayload in eventPayloads)
        {
            events.Add(AggregateEventHandler.HandleAggregateEvent(aggregate, eventPayload));
        }
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, events.ToImmutableList(), aggregate.Version));
    }
    public virtual TCommand CleanupCommandIfNeeded(TCommand command)
    {
        return command;
    }

    protected abstract IAsyncEnumerable<IApplicableEvent<TAggregatePayload>> ExecCreateCommandAsync(
        AggregateState<TAggregatePayload> aggregate,
        TCommand command);
}
