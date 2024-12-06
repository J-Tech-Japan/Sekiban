using ResultBoxes;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Pure.Exception;
using System.Collections.Immutable;
namespace Sekiban.Pure;

public interface IEvent
{
    public int Version { get; }
    public string SortableUniqueId { get; }
    public PartitionKeys PartitionKeys { get; }
    public SortableUniqueIdValue GetSortableUniqueId() => new(SortableUniqueId);
    public IEventPayload GetPayload();
}
public record Event<TEventPayload>(
    TEventPayload Payload,
    PartitionKeys PartitionKeys,
    string SortableUniqueId,
    int Version) : IEvent where TEventPayload : IEventPayload
{
    public IEventPayload GetPayload() => Payload;
}
public interface IAggregatePayload
{
}
public record EmptyAggregatePayload : IAggregatePayload
{
    public static EmptyAggregatePayload Empty => new();
}
public record Aggregate(
    IAggregatePayload Payload,
    PartitionKeys PartitionKeys,
    int Version,
    string LastSortableUniqueId) : IAggregate
{
    public static Aggregate Empty => new(new EmptyAggregatePayload(), PartitionKeys.Generate(), 0, string.Empty);
    public IAggregatePayload GetPayload() => Payload;
    public static Aggregate EmptyFromPartitionKeys(PartitionKeys keys) =>
        new(new EmptyAggregatePayload(), keys, 0, string.Empty);
    public ResultBox<Aggregate<TAggregatePayload>> ToTypedPayload<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload => Payload is TAggregatePayload typedPayload
        ? ResultBox.FromValue(
            new Aggregate<TAggregatePayload>(typedPayload, PartitionKeys, Version, LastSortableUniqueId))
        : new SekibanAggregateTypeException("Payload is not typed to " + typeof(TAggregatePayload).Name);
    public ResultBox<Aggregate> Project(IEvent ev, IAggregateProjector projector) => this with
    {
        Payload = projector.Project(Payload, ev),
        LastSortableUniqueId = ev.SortableUniqueId,
        Version = ev.Version
    };
    public ResultBox<Aggregate> Project(List<IEvent> events, IAggregateProjector projector) => ResultBox
        .FromValue(events)
        .ReduceEach(this, (ev, aggregate) => aggregate.Project(ev, projector));
}
public record Aggregate<TAggregatePayload>(
    TAggregatePayload Payload,
    PartitionKeys PartitionKeys,
    int Version,
    string LastSortableUniqueId) : IAggregate where TAggregatePayload : IAggregatePayload
{
    public IAggregatePayload GetPayload() => Payload;
}
public interface IAggregate
{
    public int Version { get; }
    public string LastSortableUniqueId { get; }
    public PartitionKeys PartitionKeys { get; }
    public OptionalValue<SortableUniqueIdValue> GetLastSortableUniqueIdValue() =>
        SortableUniqueIdValue.OptionalValue(LastSortableUniqueId);
    public IAggregatePayload GetPayload();
}
public interface IEventPayload;
public interface IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev);
    public virtual string GetVersion() => "initial";
}
public interface ICommandContext : ICommandContextCommon
{
    IAggregate ICommandContextCommon.GetAggregateCommon() => GetAggregate();
    public IAggregate GetAggregate();
}
public interface ICommandContextCommon
{
    public List<IEvent> Events { get; }
    public IAggregate GetAggregateCommon();
    public EventOrNone AppendEvent(IEventPayload eventPayload);
    internal CommandExecuted GetCommandExecuted(List<IEvent> producedEvents) => new(
        GetAggregateCommon().PartitionKeys,
        GetAggregateCommon().LastSortableUniqueId,
        producedEvents);
}
public interface ICommandContext<TAggregatePayload> : ICommandContextCommon where TAggregatePayload : IAggregatePayload
{
    public ResultBox<Aggregate<TAggregatePayload>> GetAggregate();
}
public interface ICommand
{
}
public interface ICommandHandler<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext context);
}
public interface ICommandHandler<TCommand, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<TAggregatePayload> context);
}
public interface ICommandHandlerInjection<TCommand, TInjection> where TCommand : ICommand, IEquatable<TCommand>
{
    public ResultBox<EventOrNone> Handle(TCommand command, TInjection injection, ICommandContext context);
}
public interface ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public PartitionKeys SpecifyPartitionKeys(TCommand command);
}
public interface ICommandWithHandler<TCommand, TProjector> : ICommandWithHandlerCommon<TCommand>
    where TCommand : ICommand, IEquatable<TCommand> where TProjector : IAggregateProjector, new()
{
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerInjection<TCommand, TProjector, TInject> : ICommandWithHandlerInjectionCommon<TCommand, TInject>
    where TCommand : ICommand, IEquatable<TCommand> where TProjector : IAggregateProjector, new()
{
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface ICommandGetProjector
{
    public IAggregateProjector GetProjector();
}
public interface ICommandWithHandlerCommon;
public interface ICommandWithHandlerCommon<TCommand> : ICommand,
    ICommandWithHandlerCommon,
    ICommandHandler<TCommand>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>;
public interface ICommandWithHandlerCommon<TCommand, TAggregatePayload> : ICommand,
    ICommandWithHandlerCommon,
    ICommandHandler<TCommand, TAggregatePayload>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload;
public interface ICommandWithAggregateTypeRestrictionCommon
{
    public Type GetAggregateType();
}
public interface ICommandWithAggregateTypeRestriction<TAggregatePayload> : ICommandWithAggregateTypeRestrictionCommon
    where TAggregatePayload : IAggregatePayload
{
    Type ICommandWithAggregateTypeRestrictionCommon.GetAggregateType() => typeof(TAggregatePayload);
}
public interface ICommandWithHandlerInjectionCommon<TCommand, TInjection> : ICommand,
    ICommandHandlerInjection<TCommand, TInjection>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
}
public interface
    ICommandWithHandler<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, TAggregatePayload>,
    ICommandWithAggregateTypeRestriction<TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public static class PartitionKeys<TAggregateProjector> where TAggregateProjector : IAggregateProjector, new()
{
    public static PartitionKeys Generate(string rootPartitionKey = PartitionKeys.DefaultRootPartitionKey) =>
        PartitionKeys.Generate<TAggregateProjector>(rootPartitionKey);
    public static PartitionKeys Existing(Guid aggregateId, string group = PartitionKeys.DefaultAggregateGroupName) =>
        PartitionKeys.Existing<TAggregateProjector>(aggregateId, group);
}
public record PartitionKeys(Guid AggregateId, string Group, string RootPartitionKey)
{
    public const string DefaultRootPartitionKey = "default";
    public const string DefaultAggregateGroupName = "default";
    public static PartitionKeys Generate(
        string group = DefaultAggregateGroupName,
        string rootPartitionKey = DefaultRootPartitionKey) =>
        new(Guid.CreateVersion7(), group, rootPartitionKey);
    public static PartitionKeys Generate<TAggregateProjector>(string rootPartitionKey = DefaultRootPartitionKey)
        where TAggregateProjector : IAggregateProjector =>
        new(Guid.CreateVersion7(), typeof(TAggregateProjector).Name, rootPartitionKey);
    public static PartitionKeys Existing(
        Guid aggregateId,
        string group = "default",
        string rootPartitionKey = "default") =>
        new(aggregateId, group, rootPartitionKey);
    public static PartitionKeys Existing<TAggregateProjector>(Guid aggregateId, string rootPartitionKey = "default")
        where TAggregateProjector : IAggregateProjector =>
        new(aggregateId, typeof(TAggregateProjector).Name, rootPartitionKey);
}
public record TenantPartitionKeys(string TenantCode)
{
    public static TenantPartitionKeys Tenant(string tenantCode) => new(tenantCode);

    public PartitionKeys Generate(string group = PartitionKeys.DefaultAggregateGroupName) =>
        PartitionKeys.Generate(group, TenantCode);
    public PartitionKeys Existing(Guid aggregateId, string group = PartitionKeys.DefaultAggregateGroupName) =>
        PartitionKeys.Existing(aggregateId, group, TenantCode);
}
public record CommandResponse(PartitionKeys PartitionKeys, List<IEvent> Events, int Version);
public interface ICommandExecutor
{
}
public class CommandExecutor : ICommandExecutor
{
    public IEventTypes EventTypes { get; init; } = new EmptyEventTypes();

    // CommandWithHandler 版 １クラスに定義できるが、実態は、メソッドを関数版に渡している
    public Task<ResultBox<CommandResponse>> Execute<TCommand>(TCommand command)
        where TCommand : ICommandWithHandlerCommon<TCommand>, IEquatable<TCommand> => Execute(
        command,
        command.GetProjector(),
        command.SpecifyPartitionKeys,
        command.Handle);

    public Task<ResultBox<EventOrNone>> RunHandler<TCommand, TInject, TAggregatePayload>(
        TCommand command,
        ICommandContextCommon context,
        TInject inject,
        Delegate handler) where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        (handler, context) switch
        {
            (Func<TCommand, ICommandContext, ResultBox<EventOrNone>> handler1, ICommandContext context1) => handler1(
                    command,
                    context1)
                .ToTask(),
            (Func<TCommand, TInject, ICommandContext, ResultBox<EventOrNone>> handler1, ICommandContext context1) =>
                handler1(command, inject, context1).ToTask(),
            (Func<TCommand, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler1,
                ICommandContext<TAggregatePayload> context1) => handler1(command, context1).ToTask(),
            (Func<TCommand, TInject, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler1,
                ICommandContext<TAggregatePayload> context1) => handler1(command,inject, context1).ToTask(),


            _ => ResultBox<EventOrNone>
                .FromException(
                    new SekibanCommandHandlerNotMatchException(
                        $"{handler.GetType().Name} does not match as command handler"))
                .ToTask()
        };


    public Task<ResultBox<CommandResponse>> ExecuteGeneral<TCommand>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        Delegate handler) where TCommand : ICommand =>
        specifyPartitionKeys(command)
            .ToResultBox()
            .Combine(keys => Repository.Load(keys, projector))
            .Combine(
                (partitionKeys, aggregate) => ResultBox.FromValue(new CommandContext(aggregate, projector, EventTypes)))
            .Combine(
                (partitionKeys, aggregate, context) => handler(command, context)
                    .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone)))
            .Conveyor(values => Repository.Save(values.Value4.ProducedEvents).Remap(_ => values))
            .Conveyor(
                (partitionKeys, aggregate, context, executed) => ResultBox.FromValue(
                    new CommandResponse(partitionKeys, executed.ProducedEvents, aggregate.Version)))
            .ToTask();


    // Function 版 これを使えば、CommandHandler クラスは不要
    public Task<ResultBox<CommandResponse>> Execute<TCommand>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        Func<TCommand, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
        specifyPartitionKeys(command)
            .ToResultBox()
            .Combine(keys => Repository.Load(keys, projector))
            .Combine(
                (partitionKeys, aggregate) => ResultBox.FromValue(new CommandContext(aggregate, projector, EventTypes)))
            .Combine(
                (partitionKeys, aggregate, context) => handler(command, context)
                    .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone)))
            .Conveyor(values => Repository.Save(values.Value4.ProducedEvents).Remap(_ => values))
            .Conveyor(
                (partitionKeys, aggregate, context, executed) => ResultBox.FromValue(
                    new CommandResponse(partitionKeys, executed.ProducedEvents, aggregate.Version)))
            .ToTask();

    public ExceptionOrNone VerifyAggregateType<TCommand, TAggregatePayload>(TCommand command, Aggregate aggregate)
        where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        command is ICommandWithAggregateTypeRestrictionCommon restriction &&
        restriction.GetAggregateType() != aggregate.GetPayload().GetType()
            ? ExceptionOrNone.FromException(
                new SekibanAggregateTypeRestrictionException(
                    $"To execute command {command.GetType().Name}, " +
                    $"Aggregate must be {restriction.GetAggregateType().Name}," +
                    $" but currently aggregate type is {aggregate.GetPayload().GetType().Name}"))
            : ExceptionOrNone.None;

    public Task<ResultBox<CommandResponse>>
        Execute<TCommand, TAggregatePayload>(
            TCommand command,
            IAggregateProjector projector,
            Func<TCommand, PartitionKeys> specifyPartitionKeys,
            Func<TCommand, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler)
        where TCommand : ICommand where TAggregatePayload : IAggregatePayload => specifyPartitionKeys(command)
        .ToResultBox()
        .Combine(keys => Repository.Load(keys, projector))
        .Verify((keys, aggregate) => VerifyAggregateType<TCommand, TAggregatePayload>(command, aggregate))
        .Combine(
            (partitionKeys, aggregate) =>
                ResultBox.FromValue(new CommandContext<TAggregatePayload>(aggregate, projector, EventTypes)))
        .Combine(
            (partitionKeys, aggregate, context) => handler(command, context)
                .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone)))
        .Conveyor(values => Repository.Save(values.Value4.ProducedEvents).Remap(_ => values))
        .Conveyor(
            (partitionKeys, aggregate, context, executed) =>
                ResultBox.FromValue(new CommandResponse(partitionKeys, executed.ProducedEvents, 0)))
        .ToTask();


    public Task<ResultBox<CommandResponse>> Execute<TCommand, TInject>(TCommand command, TInject inject)
        where TCommand : ICommandWithHandlerInjectionCommon<TCommand, TInject>, IEquatable<TCommand> => Execute(
        command,
        command.GetProjector(),
        command.SpecifyPartitionKeys,
        inject,
        command.Handle);

    public Task<ResultBox<CommandResponse>> Execute<TCommand, TInject>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        TInject inject,
        Func<TCommand, TInject, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand => ResultBox
        .Start
        .Conveyor(_ => specifyPartitionKeys(command).ToResultBox())
        .Combine(keys => Repository.Load(keys, projector))
        .Combine(
            (partitionKeys, aggregate) => ResultBox.FromValue(new CommandContext(aggregate, projector, EventTypes)))
        .Combine(
            (partitionKeys, aggregate, context) => handler(command, inject, context)
                .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone)))
        .Conveyor(values => Repository.Save(values.Value4.ProducedEvents).Remap(_ => values))
        .Conveyor(
            (partitionKeys, aggregate, context, executed) => ResultBox.FromValue(
                new CommandResponse(partitionKeys, executed.ProducedEvents, aggregate.Version)))
        .ToTask();

    public ResultBox<CommandExecuted> EventToCommandExecuted(
        ICommandContextCommon commandContext,
        EventOrNone eventOrNone) =>
        (eventOrNone.HasEvent
            ? EventTypes
                .GenerateTypedEvent(
                    eventOrNone.GetValue(),
                    commandContext.GetAggregateCommon().PartitionKeys,
                    SortableUniqueIdValue.GetCurrentIdFromUtc(),
                    commandContext.GetAggregateCommon().Version + 1)
                .Remap(ev => commandContext.Events.ToImmutableList().Add(ev).ToList())
            : ResultBox.FromValue(commandContext.Events)).Remap(commandContext.GetCommandExecuted);

    // public Task<ResultBox<CommandExecuted>> RunCommand<TCommand, TInject>(
    //     TCommand command,
    //     PartitionKeys keys,
    //     TInject inject,
    //     ICommandContext context,
    //     Func<TCommand, TInject, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
    //     handler(command, inject, context)
    //         .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone))
    //         .ToTask();
}
public record CommandExecuted(PartitionKeys PartitionKeys, string LastSortableUniqueId, List<IEvent> ProducedEvents);
public class CommandContext(Aggregate aggregate, IAggregateProjector projector, IEventTypes eventTypes)
    : ICommandContext
{
    public Aggregate Aggregate { get; set; } = aggregate;
    public IAggregateProjector Projector { get; } = projector;
    public IEventTypes EventTypes { get; } = eventTypes;
    public List<IEvent> Events { get; } = new();
    public IAggregate GetAggregate() => Aggregate;
    public EventOrNone AppendEvent(IEventPayload eventPayload)
    {
        var toAdd = EventTypes.GenerateTypedEvent(
            eventPayload,
            Aggregate.PartitionKeys,
            SortableUniqueIdValue.GetCurrentIdFromUtc(),
            Aggregate.Version + 1);
        if (!toAdd.IsSuccess) { return EventOrNone.Empty; }
        var ev = toAdd.GetValue();
        var aggregatePayload = Projector.Project(Aggregate.GetPayload(), toAdd.GetValue());
        var projected = Aggregate.Project(ev, Projector);
        if (projected.IsSuccess) { Aggregate = projected.GetValue(); } else { return EventOrNone.Empty; }
        Events.Add(ev);
        return EventOrNone.Empty;
    }
}
public class CommandContext<TAggregatePayload>(
    Aggregate aggregate,
    IAggregateProjector projector,
    IEventTypes eventTypes) : ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayload
{
    private CommandContext CommandContextUntyped { get; } = new(aggregate, projector, eventTypes);
    public ResultBox<Aggregate<TAggregatePayload>> GetAggregate() =>
        CommandContextUntyped.Aggregate.ToTypedPayload<TAggregatePayload>();
    public List<IEvent> Events => CommandContextUntyped.Events;
    public IAggregate GetAggregateCommon() => CommandContextUntyped.GetAggregate();
    public EventOrNone AppendEvent(IEventPayload eventPayload) => CommandContextUntyped.AppendEvent(eventPayload);
}
public class Repository
{
    public static List<IEvent> Events { get; set; } = new();


    public Func<string, IEvent> Deserializer { get; set; } = s => throw new NotImplementedException();
    public Func<IEvent, string> Serializer { get; set; } = s => throw new NotImplementedException();
    public static ResultBox<Aggregate> Load<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() => Load(partitionKeys, new TAggregateProjector());
    public static ResultBox<Aggregate> Load(PartitionKeys partitionKeys, IAggregateProjector projector) =>
        ResultBox
            .FromValue(
                Events.Where(e => e.PartitionKeys.Equals(partitionKeys)).OrderBy(e => e.SortableUniqueId).ToList())
            .Conveyor(events => Aggregate.EmptyFromPartitionKeys(partitionKeys).Project(events, projector));

    public static ResultBox<UnitValue> Save(List<IEvent> events) => ResultBox.Start.Do(() => Events.AddRange(events));
}
public interface IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        int version);
}
public class EmptyEventTypes : IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        int version) => ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException(""));
}
