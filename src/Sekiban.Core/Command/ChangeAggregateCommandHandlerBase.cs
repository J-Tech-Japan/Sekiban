using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class ChangeAggregateCommandHandlerBase<T, C> : IChangeAggregateCommandHandler<T, C>
    where T : IAggregatePayload, new() where C : ChangeAggregateCommandBase<T>, new()
{
    public async Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, Aggregate<T> aggregate)
    {
        var command = aggregateCommandDocument.Payload;

        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(C).Name);
        }
        var state = aggregate.ToState();
        // Validate Aggregate is deleted
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (command is not INoValidateCommand && state is IDeletableAggregatePayload { IsDeleted: true })
        {
            throw new SekibanAggregateNotExistsException(aggregate.AggregateId, typeof(T).Name);
        }

        // Validate Aggregate Version
        if (command is not INoValidateCommand && command.ReferenceVersion != aggregate.Version)
        {
            throw new SekibanAggregateCommandInconsistentVersionException(aggregate.AggregateId, aggregate.Version);
        }

        // Execute Command
        var eventPayloads = ExecCommandAsync(aggregate.ToState(), command);
        var events = new List<IAggregateEvent>();
        await foreach (var eventPayload in eventPayloads)
        {
            events.Add(Aggregate<T>.AddAndApplyEvent(aggregate, eventPayload));
        }
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, events.ToImmutableList(), aggregate.Version));
    }
    public Task<AggregateCommandResponse> HandleForOnlyPublishingCommandAsync(AggregateCommandDocument<C> aggregateCommandDocument, Guid aggregateId)
    {
        throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(C).Name);
    }
    public virtual C CleanupCommandIfNeeded(C command)
    {
        return command;
    }

    protected abstract IAsyncEnumerable<IChangedEvent<T>> ExecCommandAsync(AggregateState<T> aggregate, C command);
}
