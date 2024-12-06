using ResultBoxes;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Pure.Exception;
using System.Collections.Immutable;
namespace Sekiban.Pure;

public interface IEvent
{
    public long Version { get; }
    public string SortableUniqueId { get; }
    public PartitionKeys PartitionKeys { get; }
    public SortableUniqueIdValue GetSortableUniqueId() => new(SortableUniqueId);
    public IEventPayload GetPayload();
}
public record Event<TEventPayload>(
    TEventPayload Payload,
    PartitionKeys PartitionKeys,
    string SortableUniqueId,
    long Version) : IEvent where TEventPayload : IEventPayload
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
    long Version,
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
    long Version,
    string LastSortableUniqueId) : IAggregate where TAggregatePayload : IAggregatePayload
{
    public IAggregatePayload GetPayload() => Payload;
}
public interface IAggregate
{
    public long Version { get; }
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
public interface ICommandContext
{
    public List<IEvent> Events { get; }
    public IAggregate GetAggregate();
    public EventOrNone AppendEvent(IEventPayload eventPayload);
}
public interface ICommand
{
}
public interface ICommandHandler<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext context);
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
public interface ICommandWithHandlerCommon<TCommand> : ICommand,
    ICommandHandler<TCommand>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>;
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
public interface ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayload
{
    public Aggregate<TAggregatePayload> GetAggregate();
    public EventOrNone AppendEvent(IEventPayload eventPayload);
}
public interface ICommandWithHandler<TCommand, TProjector, TAggregatePayload> : ICommandWithHandlerCommon<TCommand>,
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



    // Command 版 これを使えば、CommandHandler クラスは不要
    public Task<ResultBox<CommandResponse>>
        Execute<TCommand>(
            TCommand command,
            IAggregateProjector projector,
            Func<TCommand, PartitionKeys> specifyPartitionKeys,
            Func<TCommand, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand => ResultBox
        .Start
        .Conveyor(_ => specifyPartitionKeys(command).ToResultBox())
        .Combine(keys => Repository.Load(keys, projector))
        .Verify(
            (keys, aggregate) =>
                command is ICommandWithAggregateTypeRestrictionCommon restriction &&
                restriction.GetAggregateType() != aggregate.GetPayload().GetType()
                    ? ExceptionOrNone.FromException(
                        new SekibanAggregateTypeRestrictionException(
                            $"To execute command {command.GetType().Name}, " +
                            $"Aggregate must be {restriction.GetAggregateType().Name}," +
                            $" but currently aggregate type is {aggregate.GetPayload().GetType().Name}"))
                    : ExceptionOrNone.None)
        .Combine(
            (partitionKeys, aggregate) => ResultBox.FromValue(new CommandContext(aggregate, projector, EventTypes)))
        .Combine((partitionKeys, aggregate, context) => RunCommand(command, partitionKeys, context, handler))
        .Conveyor(values => Repository.Save(values.Value4.Events).Remap(_ => values))
        .Conveyor(
            (partitionKeys, aggregate, context, executed) =>
                ResultBox.FromValue(new CommandResponse(partitionKeys, executed.Events, 0)));

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
        .Combine((partitionKeys, aggregate, context) => RunCommand(command, partitionKeys, inject, context, handler))
        .Conveyor(values => Repository.Save(values.Value4.Events).Remap(_ => values))
        .Conveyor(
            (partitionKeys, aggregate, context, executed) =>
                ResultBox.FromValue(new CommandResponse(partitionKeys, executed.Events, 0)));

    public ResultBox<CommandExecuted> EventToCommandExecuted(ICommandContext commandContext, EventOrNone eventOrNone) =>
        (eventOrNone.HasEvent
            ? EventTypes
                .GenerateTypedEvent(
                    eventOrNone.GetValue(),
                    commandContext.GetAggregate().PartitionKeys,
                    SortableUniqueIdValue.GetCurrentIdFromUtc(),
                    commandContext.GetAggregate().Version + 1)
                .Remap(ev => commandContext.Events.ToImmutableList().Add(ev).ToList())
            : ResultBox.FromValue(commandContext.Events)).Remap(
            events => new CommandExecuted(commandContext.GetAggregate(), events));

    public Task<ResultBox<CommandExecuted>> RunCommand<TCommand>(
        TCommand command,
        PartitionKeys keys,
        ICommandContext context,
        Func<TCommand, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
        handler(command, context).Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone)).ToTask();

    public Task<ResultBox<CommandExecuted>> RunCommand<TCommand, TInject>(
        TCommand command,
        PartitionKeys keys,
        TInject inject,
        ICommandContext context,
        Func<TCommand, TInject, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
        handler(command, inject, context)
            .Conveyor(eventOrNone => EventToCommandExecuted(context, eventOrNone))
            .ToTask();

    public record CommandExecuted(IAggregate Aggregate, List<IEvent> Events);
}
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
        long version);
}
public class EmptyEventTypes : IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        long version) => ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException(""));
}
