using ResultBoxes;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Pure.Exception;
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
public interface ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayload
{
    public List<IEvent> Events { get; }
    internal IAggregate GetAggregateCommon();
    public EventOrNone AppendEvent(IEventPayload eventPayload);
    internal CommandExecuted GetCommandExecuted(List<IEvent> producedEvents) => new(
        GetAggregateCommon().PartitionKeys,
        GetAggregateCommon().LastSortableUniqueId,
        producedEvents);
    public ResultBox<Aggregate<TAggregatePayload>> GetAggregate();
}
public interface ICommand
{
}
public record NoInjection;
public interface ICommandHandler<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<IAggregatePayload> context);
}
public interface
    ICommandHandler<TCommand, TAggregatePayload> : ICommandHandlerCommon<TCommand, NoInjection, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<TAggregatePayload> context);
}
public interface ICommandHandlerCommon
{
}
public interface ICommandHandlerCommon<TCommand, TInjection, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
}
public interface
    ICommandHandlerInjection<TCommand, TInjection, TAggregatePayload> : ICommandHandlerCommon<TCommand, TInjection,
    TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public ResultBox<EventOrNone> Handle(
        TCommand command,
        TInjection injection,
        ICommandContext<TAggregatePayload> context);
}
public interface ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public PartitionKeys SpecifyPartitionKeys(TCommand command);
}
public interface
    ICommandWithHandler<TCommand, TProjector> : ICommandWithHandlerCommon<TCommand, NoInjection, IAggregatePayload>,
    ICommandHandler<TCommand, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
    Delegate ICommandWithHandlerCommon.GetHandler() => Handle;
}
public interface
    ICommandWithHandlerInjection<TCommand, TProjector, TInject> :
    ICommandWithHandlerCommon<TCommand, TInject, IAggregatePayload>,
    ICommandHandlerInjection<TCommand, TInject, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
    Delegate ICommandWithHandlerCommon.GetHandler() => Handle;
}
public interface ICommandGetProjector
{
    public IAggregateProjector GetProjector();
}
public interface ICommandWithHandlerCommon : ICommand
{
    public Delegate GetHandler();
}
public interface ICommandWithHandlerCommon<TCommand, TInjection, TAggregatePayload> : ICommandWithHandlerCommon,
    ICommand,
    ICommandHandlerCommon<TCommand, TInjection, TAggregatePayload>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload
{
}
public interface
    ICommandWithHandler<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, NoInjection, TAggregatePayload>,
    ICommandHandler<TCommand, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
    Delegate ICommandWithHandlerCommon.GetHandler() => Handle;
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
public interface ICommandExecutor;
public class CommandExecutor : ICommandExecutor
{
    public IEventTypes EventTypes { get; init; } = new EmptyEventTypes();
    public Task<ResultBox<CommandResponse>> ExecuteWithFunction<TCommand>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        Func<TCommand, ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
        ExecuteGeneral<TCommand, NoInjection, IAggregatePayload>(
            command,
            projector,
            specifyPartitionKeys,
            OptionalValue<NoInjection>.Empty,
            handler);
    public Task<ResultBox<CommandResponse>> ExecuteWithFunction<TCommand, TInjection>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        TInjection inject,
        Func<TCommand, ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
        ExecuteGeneral<TCommand, TInjection, IAggregatePayload>(
            command,
            projector,
            specifyPartitionKeys,
            OptionalValue<TInjection>.FromValue(inject),
            handler);
    public Task<ResultBox<CommandResponse>> ExecuteWithFunction<TCommand, TInjection, TAggregatePayload>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        TInjection inject,
        Func<TCommand, TInjection, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler)
        where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        ExecuteGeneral<TCommand, TInjection, TAggregatePayload>(
            command,
            projector,
            specifyPartitionKeys,
            OptionalValue<TInjection>.FromValue(inject),
            handler);
    public Task<ResultBox<CommandResponse>> ExecuteWithFunction<TCommand, TAggregatePayload>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        Func<TCommand, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler) where TCommand : ICommand
        where TAggregatePayload : IAggregatePayload =>
        ExecuteGeneral<TCommand, NoInjection, TAggregatePayload>(
            command,
            projector,
            specifyPartitionKeys,
            OptionalValue<NoInjection>.Empty,
            handler);

    #region Private Methods
    public Task<ResultBox<CommandResponse>> ExecuteGeneral<TCommand, TInject, TAggregatePayload>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        OptionalValue<TInject> inject,
        Delegate handler) where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        specifyPartitionKeys(command)
            .ToResultBox()
            .Combine(keys => Repository.Load(keys, projector))
            .Verify((keys, aggregate) => VerifyAggregateType<TCommand, TAggregatePayload>(command, aggregate))
            .Combine(
                (partitionKeys, aggregate) => ResultBox.FromValue(
                    new CommandContext<TAggregatePayload>(aggregate, projector, EventTypes)))
            .Combine(
                (partitionKeys, aggregate, context) => RunHandler(command, context, inject, handler)
                    .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone)))
            .Conveyor(values => Repository.Save(values.Value4.ProducedEvents).Remap(_ => values))
            .Conveyor(
                (partitionKeys, aggregate, context, executed) => ResultBox.FromValue(
                    new CommandResponse(partitionKeys, executed.ProducedEvents, aggregate.Version)));
    private ExceptionOrNone VerifyAggregateType<TCommand, TAggregatePayload>(TCommand command, Aggregate aggregate)
        where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        aggregate.GetPayload() is not TAggregatePayload
            ? ExceptionOrNone.FromException(
                new SekibanAggregateTypeRestrictionException(
                    $"To execute command {command.GetType().Name}, " +
                    $"Aggregate must be {aggregate.GetPayload().GetType().Name}," +
                    $" but currently aggregate type is {aggregate.GetPayload().GetType().Name}"))
            : ExceptionOrNone.None;

    private Task<ResultBox<EventOrNone>> RunHandler<TCommand, TInject, TAggregatePayload>(
        TCommand command,
        ICommandContext<TAggregatePayload> context,
        OptionalValue<TInject> inject,
        Delegate handler) where TCommand : ICommand where TAggregatePayload : IAggregatePayload =>
        (handler, inject.HasValue) switch
        {
            (Func<TCommand, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler1, _) => handler1(
                    command,
                    context)
                .ToTask(),
            (Func<TCommand, TInject, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> handler1, true) =>
                handler1(command, inject.GetValue(), context).ToTask(),
            _ => ResultBox<EventOrNone>
                .FromException(
                    new SekibanCommandHandlerNotMatchException(
                        $"{handler.GetType().Name} does not match as command handler"))
                .ToTask()
        };
    private ResultBox<CommandExecuted> EventToCommandExecuted<TAggregatePayload>(
        ICommandContext<TAggregatePayload> commandContext,
        EventOrNone eventOrNone) where TAggregatePayload : IAggregatePayload =>
        (eventOrNone.HasEvent
            ? EventTypes
                .GenerateTypedEvent(
                    eventOrNone.GetValue(),
                    commandContext.GetAggregateCommon().PartitionKeys,
                    SortableUniqueIdValue.GetCurrentIdFromUtc(),
                    commandContext.GetAggregateCommon().Version + 1)
                .Remap(ev => commandContext.Events.Append(ev).ToList())
            : ResultBox.FromValue(commandContext.Events)).Remap(commandContext.GetCommandExecuted);
    #endregion
}
public record CommandExecuted(PartitionKeys PartitionKeys, string LastSortableUniqueId, List<IEvent> ProducedEvents);
public class CommandContext<TAggregatePayload>(
    Aggregate aggregate,
    IAggregateProjector projector,
    IEventTypes eventTypes) : ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayload
{
    public Aggregate Aggregate { get; set; } = aggregate;
    public IAggregateProjector Projector { get; } = projector;
    public IEventTypes EventTypes { get; } = eventTypes;
    public List<IEvent> Events { get; } = new();
    public IAggregate GetAggregateCommon() => Aggregate;
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
    public ResultBox<Aggregate<TAggregatePayload>> GetAggregate() => Aggregate.ToTypedPayload<TAggregatePayload>();
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
