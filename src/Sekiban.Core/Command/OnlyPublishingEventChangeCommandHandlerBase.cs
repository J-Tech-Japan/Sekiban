// using Sekiban.Core.Aggregate;
// using Sekiban.Core.Event;
// using Sekiban.Core.Exceptions;
// using System.Collections.Immutable;
// using EventHelper = Sekiban.Core.Aggregate.EventHelper;
// namespace Sekiban.Core.Command;
//
// public abstract class
//     EventPublishOnlyIVersionValidationCommandHandlerBase<TAggregatePayload, TCommand> : IChangeCommandHandler<TAggregatePayload, TCommand>
//     where TAggregatePayload : IAggregatePayload, new()
//     where TCommand : IOnlyPublishingCommandBase<TAggregatePayload>, new()
// {
//     private List<IEvent> Events { get; } = new();
//     protected Guid AggregateId { get; set; } = Guid.Empty;
//
//     public Task<CommandResponse> HandleAsync(
//         CommandDocument<TCommand> commandDocument,
//         Aggregate<TAggregatePayload> aggregate) =>
//         throw new SekibanCanNotExecuteRegularChangeCommandException(typeof(TCommand).Name);
//     public async Task<CommandResponse> HandleForOnlyPublishingCommandAsync(
//         CommandDocument<TCommand> commandDocument,
//         Guid aggregateId)
//     {
//         AggregateId = aggregateId;
//         var eventPayloads = HandleCommandAsync(aggregateId, commandDocument.Payload);
//         var events = new List<IEvent>();
//         await foreach (var eventPayload in eventPayloads)
//         {
//             events.Add(
//                 EventHelper.GenerateEventToSave<IApplicableEvent<TAggregatePayload>, TAggregatePayload>(
//                     aggregateId,
//                     eventPayload));
//         }
//         await Task.CompletedTask;
//         return new CommandResponse(aggregateId, events.ToImmutableList(), 0);
//     }
//     public virtual TCommand CleanupCommandIfNeeded(TCommand command) => command;
//     protected abstract IAsyncEnumerable<IApplicableEvent<TAggregatePayload>> HandleCommandAsync(Guid aggregateId, TCommand command);
// }



