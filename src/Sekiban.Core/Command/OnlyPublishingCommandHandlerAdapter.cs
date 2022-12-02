using System.Collections.Immutable;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;

namespace Sekiban.Core.Command;

public class OnlyPublishingCommandHandlerAdapter<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new()
    where TCommand : IOnlyPublishingCommand<TAggregatePayload>
{
    public async Task<CommandResponse> HandleCommandAsync(
        CommandDocument<TCommand> commandDocument,
        ICommandHandlerCommon<TAggregatePayload, TCommand> handler,
        Guid aggregateId)
    {
        if (handler is not IOnlyPublishingCommandHandler<TAggregatePayload, TCommand> publishHandler)
            throw new SekibanCommandHandlerNotMatchException(
                handler.GetType().Name + "handler should inherit " + typeof(IOnlyPublishingCommandHandler<,>).Name);
        var events = new List<IEvent>();
        await foreach (var eventPayload in publishHandler.HandleCommandAsync(aggregateId, commandDocument.Payload))
            events.Add(
                EventHelper.GenerateEventToSave<IEventPayload<TAggregatePayload>, TAggregatePayload>(
                    aggregateId,
                    eventPayload));
        await Task.CompletedTask;
        return new CommandResponse(aggregateId, events.ToImmutableList(), 0);
    }
}
