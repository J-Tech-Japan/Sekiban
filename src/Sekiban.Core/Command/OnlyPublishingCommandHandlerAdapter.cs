using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

// ReSharper disable once InvalidXmlDocComment
/// <summary>
///     System use for internal handler for <see cref="IOnlyPublishingCommand" />
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TCommand"></typeparam>
public class OnlyPublishingCommandHandlerAdapter<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : IOnlyPublishingCommand<TAggregatePayload>
{
    public async Task<CommandResponse> HandleCommandAsync(
        CommandDocument<TCommand> commandDocument,
        ICommandHandlerCommon<TAggregatePayload, TCommand> handler,
        Guid aggregateId,
        string rootPartitionKey)
    {
        if (handler is not IOnlyPublishingCommandHandler<TAggregatePayload, TCommand> publishHandler)
        {
            throw new SekibanCommandHandlerNotMatchException(
                handler.GetType().Name + "handler should inherit " + typeof(IOnlyPublishingCommandHandler<,>).Name);
        }
        var events = new List<IEvent>();
        await foreach (var eventPayload in publishHandler.HandleCommandAsync(aggregateId, commandDocument.Payload))
        {
            events.Add(
                EventHelper.GenerateEventToSave<IEventPayloadApplicableTo<TAggregatePayload>, TAggregatePayload>(
                    aggregateId,
                    rootPartitionKey,
                    eventPayload));
        }
        await Task.CompletedTask;
        return new CommandResponse(aggregateId, events.ToImmutableList(), 0, events.Max(m => m.SortableUniqueId));
    }
}
