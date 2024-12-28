using ResultBoxes;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Shared;
using Sekiban.Pure.Exception;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
public interface IAggregatePayload;
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
public interface ICommand;
public interface ICommandWithAggregateRestriction<TAggregatePayload> : ICommand
    where TAggregatePayload : IAggregatePayload;
public record NoInjection
{
    public static NoInjection Empty => new();
}
public interface
    ICommandHandler<TCommand, TAggregatePayload> : ICommandHandlerCommon<TCommand, NoInjection, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<TAggregatePayload> context);
}
public interface
    ICommandHandlerAsync<TCommand, TAggregatePayload> : ICommandHandlerCommon<TCommand, NoInjection, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public Task<ResultBox<EventOrNone>> HandleAsync(TCommand command, ICommandContext<TAggregatePayload> context);
}
public interface
    ICommandHandlerCommon<TCommand, TInjection, TAggregatePayload> : ICommandWithAggregateRestriction<TAggregatePayload>
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
public interface
    ICommandHandlerInjectionAsync<TCommand, TInjection, TAggregatePayload> : ICommandHandlerCommon<TCommand, TInjection,
    TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public Task<ResultBox<EventOrNone>> HandleAsync(
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
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandler<,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerAsync<TCommand, TProjector> :
    ICommandWithHandlerCommon<TCommand, NoInjection, IAggregatePayload>,
    ICommandHandlerAsync<TCommand, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerAsync<,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerInjection<TCommand, TProjector, TInject> :
    ICommandWithHandlerCommon<TCommand, TInject, IAggregatePayload>,
    ICommandHandlerInjection<TCommand, TInject, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjection<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerInjectionAsync<TCommand, TProjector, TInject> :
    ICommandWithHandlerCommon<TCommand, TInject, IAggregatePayload>,
    ICommandHandlerInjectionAsync<TCommand, TInject, IAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjectionAsync<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerInjection<TCommand, TProjector, TInject, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, TInject, TAggregatePayload>,
    ICommandHandlerInjection<TCommand, TInject, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjection<,,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerInjectionAsync<TCommand, TProjector, TInject, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, TInject, TAggregatePayload>,
    ICommandHandlerInjectionAsync<TCommand, TInject, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerInjectionAsync<,,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface ICommandGetProjector
{
    public IAggregateProjector GetProjector();
}
public interface ICommandWithHandlerCommon<TCommand, TInjection, TAggregatePayload> : ICommand,
    ICommandHandlerCommon<TCommand, TInjection, TAggregatePayload>,
    ICommandGetProjector,
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload;
public interface
    ICommandWithHandler<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, NoInjection, TAggregatePayload>,
    ICommandHandler<TCommand, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandler<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public interface
    ICommandWithHandlerAsync<TCommand, TProjector, TAggregatePayload> :
    ICommandWithHandlerCommon<TCommand, NoInjection, TAggregatePayload>,
    ICommandHandlerAsync<TCommand, TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ICommandWithHandlerAsync<,,>))]
    IAggregateProjector ICommandGetProjector.GetProjector() => new TProjector();
}
public static class PartitionKeys<TAggregateProjector> where TAggregateProjector : IAggregateProjector, new()
{
    public static PartitionKeys Generate(string rootPartitionKey = PartitionKeys.DefaultRootPartitionKey) =>
        PartitionKeys.Generate<TAggregateProjector>(rootPartitionKey);
    public static PartitionKeys Existing(Guid aggregateId, string group = PartitionKeys.DefaultAggregateGroupName) =>
        PartitionKeys.Existing<TAggregateProjector>(aggregateId, group);
}
public static class GuidExtensions
{

    public static Guid CreateVersion7()
    {
#if NET9_0
        return Guid.CreateVersion7();
#else
        return Guid.NewGuid();
#endif
    }
}
public record PartitionKeys(Guid AggregateId, string Group, string RootPartitionKey)
{
    public const string DefaultRootPartitionKey = "default";
    public const string DefaultAggregateGroupName = "default";
    public static PartitionKeys Generate(
        string group = DefaultAggregateGroupName,
        string rootPartitionKey = DefaultRootPartitionKey) =>
        new(GuidExtensions.CreateVersion7(), group, rootPartitionKey);
    public static PartitionKeys Generate<TAggregateProjector>(string rootPartitionKey = DefaultRootPartitionKey)
        where TAggregateProjector : IAggregateProjector =>
        new(GuidExtensions.CreateVersion7(), typeof(TAggregateProjector).Name, rootPartitionKey);
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

    public Task<ResultBox<CommandResponse>> ExecuteWithResource<TCommand>(
        TCommand command,
        ICommandResource<TCommand> resource) where TCommand : ICommand, IEquatable<TCommand> =>
        ExecuteGeneralNonGeneric(
            command,
            resource.GetProjector(),
            resource.GetSpecifyPartitionKeysFunc(),
            resource.GetInjection(),
            resource.GetHandler(),
            resource.GetAggregatePayloadType());

    #region Private Methods
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(CommandExecutor))]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
        Justification = "<Pending>")]
    public async Task<ResultBox<CommandResponse>> ExecuteGeneralNonGeneric(
        ICommand command,
        IAggregateProjector projector,
        Delegate specifyPartitionKeys,
        object? inject,
        Delegate handler,
        OptionalValue<Type> aggregatePayloadType)
    {
        var commandType = command.GetType();
        var injectType = inject is not null ? inject.GetType() : typeof(NoInjection);
        var optionalType = typeof(OptionalValue<>).MakeGenericType(injectType);
        var optionalMethod = optionalType?.GetMethod(
            nameof(OptionalValue.FromValue),
            BindingFlags.Static | BindingFlags.Public);
        var injectValue = inject is not null
            ? optionalMethod?.Invoke(null, new[] { inject })
            : OptionalValue<NoInjection>.Empty;
        var aggregatePayloadTypeValue = aggregatePayloadType.HasValue
            ? aggregatePayloadType.GetValue()
            : typeof(IAggregatePayload);
        var method = GetType().GetMethod(nameof(ExecuteGeneral), BindingFlags.Public | BindingFlags.Instance);
        if (method is null) { return new SekibanCommandHandlerNotMatchException("Method not found"); }
        var genericMethod = method.MakeGenericMethod(commandType, injectType, aggregatePayloadTypeValue);
        var result = genericMethod.Invoke(
            this,
            new[] { command, projector, specifyPartitionKeys, injectValue, handler });
        if (result is Task<ResultBox<CommandResponse>> task) { return await task; }
        return new SekibanCommandHandlerNotMatchException("Result is not Task<ResultBox<CommandResponse>>");
    }


    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(CommandExecutor))]
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
                    new CommandResponse(
                        partitionKeys,
                        executed.ProducedEvents,
                        executed.ProducedEvents.Count > 0
                            ? executed.ProducedEvents.Last().Version
                            : aggregate.Version)));
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
            (Func<TCommand, ICommandContext<TAggregatePayload>, Task<ResultBox<EventOrNone>>> handler1, _) => handler1(
                command,
                context),
            (Func<TCommand, TInject, ICommandContext<TAggregatePayload>, Task<ResultBox<EventOrNone>>> handler1, true)
                => handler1(command, inject.GetValue(), context),
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
                    SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
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
            SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
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
public interface ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc();
    public OptionalValue<Type> GetAggregatePayloadType();
    public Type GetCommandType();
    public IAggregateProjector GetProjector();
    public object? GetInjection();
    public Delegate GetHandler();
}
public record CommandResource<TCommand, TProjector, TAggregatePayload>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    Func<TCommand, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => NoInjection.Empty;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => typeof(TAggregatePayload);
}
public record CommandResourceTask<TCommand, TProjector, TAggregatePayload>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    Func<TCommand, ICommandContext<TAggregatePayload>, Task<ResultBox<EventOrNone>>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => NoInjection.Empty;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => typeof(TAggregatePayload);
}
public record CommandResource<TCommand, TProjector>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    Func<TCommand, ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => NoInjection.Empty;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => OptionalValue<Type>.Empty;
}
public record CommandResourceTask<TCommand, TProjector>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    Func<TCommand, ICommandContext<IAggregatePayload>, Task<ResultBox<EventOrNone>>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object GetInjection() => NoInjection.Empty;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => OptionalValue<Type>.Empty;
}
public record CommandResourceWithInject<TCommand, TProjector, TInject>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    TInject? Injection,
    Func<TCommand, TInject, ICommandContext<IAggregatePayload>, ResultBox<EventOrNone>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => Injection;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => OptionalValue<Type>.Empty;
}
public record CommandResourceWithInjectTask<TCommand, TProjector, TInject>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    TInject? Injection,
    Func<TCommand, TInject, ICommandContext<IAggregatePayload>, Task<ResultBox<EventOrNone>>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => Injection;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => OptionalValue<Type>.Empty;
}
public record CommandResourceWithInject<TCommand, TProjector, TAggregatePayload, TInject>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    TInject? Injection,
    Func<TCommand, TInject, ICommandContext<TAggregatePayload>, ResultBox<EventOrNone>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => Injection;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => typeof(TAggregatePayload);
}
public record CommandResourceWithInjectTask<TCommand, TProjector, TAggregatePayload, TInject>(
    Func<TCommand, PartitionKeys> SpecifyPartitionKeys,
    TInject? Injection,
    Func<TCommand, TInject, ICommandContext<TAggregatePayload>, Task<ResultBox<EventOrNone>>> Handler)
    : ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
    where TAggregatePayload : IAggregatePayload
    where TProjector : IAggregateProjector, new()
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc() => SpecifyPartitionKeys;
    public Type GetCommandType() => typeof(TCommand);
    public IAggregateProjector GetProjector() => new TProjector();
    public object? GetInjection() => Injection;
    public Delegate GetHandler() => Handler;
    public OptionalValue<Type> GetAggregatePayloadType() => typeof(TAggregatePayload);
}
