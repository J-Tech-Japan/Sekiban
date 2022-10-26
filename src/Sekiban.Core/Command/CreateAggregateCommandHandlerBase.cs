using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
using System.Reflection;
namespace Sekiban.Core.Command;

public abstract class CreateAggregateCommandHandlerBase<T, C> : ICreateAggregateCommandHandler<T, C>
    where T : IAggregatePayload, new() where C : ICreateAggregateCommand<T>, new()
{

    public async Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> command, Aggregate<T> aggregate)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(C).Name);
        }
        var eventPayloads = ExecCreateCommandAsync(aggregate.ToState(), command.Payload);
        var events = new List<IAggregateEvent>();
        await foreach (var eventPayload in eventPayloads)
        {
            var aggregateType = aggregate.GetType();
            var methodName = nameof(Aggregate<T>.AddAndApplyEvent);
            var aggregateMethodBase = aggregateType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            var aggregateMethod = aggregateMethodBase?.MakeGenericMethod(eventPayload.GetType());
            events.Add(
                aggregateMethod?.Invoke(aggregate, new object?[] { eventPayload }) as IAggregateEvent ??
                throw new SekibanEventFailedToActivateException());
        }
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, events.ToImmutableList(), aggregate.Version));
    }
    public virtual C CleanupCommandIfNeeded(C command)
    {
        return command;
    }

    protected abstract IAsyncEnumerable<IApplicableEvent<T>> ExecCreateCommandAsync(AggregateState<T> aggregate, C command);
}
