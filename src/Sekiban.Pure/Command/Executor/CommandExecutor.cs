using ResultBoxes;
using Sekiban.Core.Shared;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Validations;
using System.Diagnostics.CodeAnalysis;
namespace Sekiban.Pure.Command.Executor;

public class CommandExecutor
{
    public IEventTypes EventTypes { get; init; } = new EmptyEventTypes();
    public Repository Repository { get; init; } = new();

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(CommandExecutor))]
    public Task<ResultBox<CommandResponse>> ExecuteGeneral<TCommand, TAggregatePayload>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        Delegate handler,
        CommandMetadata commandMetadata) where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        specifyPartitionKeys(command)
            .ToResultBox()
            .Conveyor(
                partitionKeys => ExecuteGeneralWithPartitionKeys<TCommand, TAggregatePayload>(
                    command,
                    projector,
                    partitionKeys,
                    handler,
                    commandMetadata,
                    (pk, pj) => Repository.Load(pk, pj).ToTask(),
                    (_, events) => Repository.Save(events).ToTask()));

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(CommandExecutor))]
    public Task<ResultBox<CommandResponse>> ExecuteGeneralWithPartitionKeys<TCommand, TAggregatePayload>(
        TCommand command,
        IAggregateProjector projector,
        PartitionKeys partitionKeys,
        Delegate handler,
        CommandMetadata commandMetadata,
        Func<PartitionKeys, IAggregateProjector, Task<ResultBox<Aggregate>>> loader,
        Func<string, List<IEvent>, Task<ResultBox<List<IEvent>>>> saver) where TCommand : ICommand
        where TAggregatePayload : IAggregatePayload =>
        ResultBox
            .Start
            .Conveyor(_ => command.ValidateProperties().ToList().ToResultBox())
            .Verify(errors => errors.Count == 0 ? ExceptionOrNone.None : new SekibanValidationErrorsException(errors))
            .Conveyor(
                _ => CreateCommandContextWithoutState<TCommand, TAggregatePayload>(
                    command,
                    partitionKeys,
                    projector,
                    handler,
                    loader,
                    commandMetadata))
            .Combine(
                context => RunHandler<TCommand, TAggregatePayload>(command, context, handler)
                    .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone)))
            .Conveyor(
                values => saver(values.Value1.OriginalSortableUniqueId, values.Value2.ProducedEvents)
                    .Remap(savedEvent => TwoValues.FromValues(values.Value1, savedEvent)))
            .Conveyor(
                (context, savedEvents) => ResultBox.FromValue(
                    new CommandResponse(
                        partitionKeys,
                        savedEvents,
                        savedEvents.Count > 0 ? savedEvents.Last().Version : context.GetCurrentVersion())));


    private Task<ResultBox<ICommandContextWithoutState>> CreateCommandContextWithoutState<TCommand, TAggregatePayload>(
        TCommand command,
        PartitionKeys partitionKeys,
        IAggregateProjector projector,
        Delegate handler,
        Func<PartitionKeys, IAggregateProjector, Task<ResultBox<Aggregate>>> loader,
        CommandMetadata commandMetadata) where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        handler switch
        {
            Func<TCommand, ICommandContextWithoutState, ResultBox<EventOrNone>> handler1 => ResultBox<
                    ICommandContextWithoutState>
                .FromValue(new CommandContextWithoutState(partitionKeys, EventTypes, commandMetadata))
                .ToTask(),
            Func<TCommand, ICommandContextWithoutState, Task<ResultBox<EventOrNone>>> handler1 => ResultBox<
                    ICommandContextWithoutState>
                .FromValue(new CommandContextWithoutState(partitionKeys, EventTypes, commandMetadata))
                .ToTask(),
            _ => ResultBox
                .Start
                .Conveyor(keys => loader(partitionKeys, projector))
                .Verify(aggregate => VerifyAggregateType<TCommand, TAggregatePayload>(command, aggregate))
                .Conveyor(
                    aggregate => ResultBox<ICommandContextWithoutState>.FromValue(
                        new CommandContext<TAggregatePayload>(aggregate, projector, EventTypes, commandMetadata)))
        };

    private ExceptionOrNone VerifyAggregateType<TCommand, TAggregatePayload>(TCommand command, Aggregate aggregate)
        where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        aggregate.GetPayload() is not TAggregatePayload
            ? ExceptionOrNone.FromException(
                new SekibanAggregateTypeRestrictionException(
                    $"To execute command {command.GetType().Name}, " +
                    $"Aggregate must be {aggregate.GetPayload().GetType().Name}," +
                    $" but currently aggregate type is {aggregate.GetPayload().GetType().Name}"))
            : ExceptionOrNone.None;

    private Task<ResultBox<EventOrNone>> RunHandler<TCommand, TAggregatePayload>(
        TCommand command,
        ICommandContextWithoutState context,
        Delegate handler) where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        (handler, context) switch
        {
            (Func<TCommand, ICommandContextWithoutState, ResultBox<EventOrNone>> handler1, { } contextWithout) =>
                handler1(command, contextWithout).ToTask(),
            (Func<TCommand, ICommandContextWithoutState, Task<ResultBox<EventOrNone>>> handler1, { } contextWithout) =>
                handler1(command, contextWithout),
            (Func<TCommand, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler1,
                ICommandContext<TAggregatePayload> stateContext) => handler1(command, stateContext).ToTask(),
            (Func<TCommand, ICommandContext<TAggregatePayload>, Task<ResultBox<EventOrNone>>> handler1,
                ICommandContext<TAggregatePayload> stateContext) => handler1(command, stateContext),
            _ => ResultBox<EventOrNone>
                .FromException(
                    new SekibanCommandHandlerNotMatchException(
                        $"{handler.GetType().Name} does not match as command handler"))
                .ToTask()
        };
    private ResultBox<CommandExecuted> EventToCommandExecuted(
        ICommandContextWithoutState commandContext,
        EventOrNone eventOrNone) =>
        (eventOrNone.HasEvent
            ? EventTypes
                .GenerateTypedEvent(
                    eventOrNone.GetValue(),
                    commandContext.GetPartitionKeys(),
                    SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
                    commandContext.GetNextVersion(),
                    commandContext.EventMetadata)
                .Remap(ev => commandContext.Events.Append(ev).ToList())
            : ResultBox.FromValue(commandContext.Events)).Remap(commandContext.GetCommandExecuted);
}
