// using Sekiban.Core.Aggregate;
// using Sekiban.Core.Event;
// using Sekiban.Core.Exceptions;
// using System.Collections.Immutable;
// using EventHelper = Sekiban.Core.Aggregate.EventHelper;
// namespace Sekiban.Core.Command;
//
// public abstract class ChangeCommandHandlerBase<TAggregatePayload, TCommand> : IChangeCommandHandler<TAggregatePayload, TCommand>
//     where TAggregatePayload : IAggregatePayload, new() where TCommand : ChangeCommandBase<TAggregatePayload>, new()
// {
//     private readonly List<IEvent> _events = new();
//     private Aggregate<TAggregatePayload>? _aggregate;
//     public async Task<CommandResponse> HandleAsync(
//         CommandDocument<TCommand> commandDocument,
//         Aggregate<TAggregatePayload> aggregate)
//     {
//         var command = commandDocument.Payload;
//         _aggregate = new Aggregate<TAggregatePayload>
//             { AggregateId = aggregate.AggregateId };
//         _aggregate.ApplySnapshot(aggregate.ToState());
//         if (command is IOnlyPublishingCommand)
//         {
//             throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
//         }
//         var state = aggregate.ToState();
//         // Validate AddAggregate is deleted
//         if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
//         {
//             throw new SekibanAggregateAlreadyDeletedException();
//         }
//
//         // Validate AddAggregate Version
//         if (command is not INoValidateCommand && command.ReferenceVersion != aggregate.Version)
//         {
//             throw new SekibanCommandInconsistentVersionException(
//                 aggregate.AggregateId,
//                 command.ReferenceVersion,
//                 aggregate.Version);
//         }
//
//         // Execute Command
//         var eventPayloads = HandleCommandAsync(GetAggregateState, command);
//         await foreach (var eventPayload in eventPayloads)
//         {
//             _events.Add(EventHelper.HandleEvent(aggregate, eventPayload));
//         }
//         return await Task.FromResult(new CommandResponse(aggregate.AggregateId, _events.ToImmutableList(), aggregate.Version));
//     }
//     public Task<CommandResponse> HandleForOnlyPublishingCommandAsync(
//         CommandDocument<TCommand> commandDocument,
//         Guid aggregateId) => throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
//     public virtual TCommand CleanupCommandIfNeeded(TCommand command) => command;
//
//     private AggregateState<TAggregatePayload> GetAggregateState()
//     {
//         if (_aggregate is null)
//         {
//             throw new SekibanCommandHandlerAggregateNullException();
//         }
//         var state = _aggregate.ToState();
//         var aggregate = new Aggregate<TAggregatePayload>();
//         aggregate.ApplySnapshot(state);
//         foreach (var ev in _events)
//         {
//             aggregate.ApplyEvent(ev);
//         }
//         state = aggregate.ToState();
//         return state;
//     }
//
//     protected abstract IAsyncEnumerable<IApplicableEvent<TAggregatePayload>> HandleCommandAsync(
//         Func<AggregateState<TAggregatePayload>> getAggregateState,
//         TCommand command);
// }


