using ResultBoxes;
using System.Collections.Immutable;
namespace Sekiban.Pure;

public interface IEvent
{
    public IEventPayload GetPayload();
}
public record Event<TEventPayload>(TEventPayload Payload) : IEvent where TEventPayload : IEventPayload
{
    public IEventPayload GetPayload() => Payload;
}
public interface IAggregatePayload
{
}
public record EmptyAggregatePayload : IAggregatePayload;
public record Aggregate<TAggregatePayload>(TAggregatePayload Payload)
    : IAggregate where TAggregatePayload : IAggregatePayload
{
    public IAggregatePayload GetPayload() => Payload;
}
public interface IAggregate
{
    public IAggregatePayload GetPayload();
}
public interface IEventPayload
{
}
public interface IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev);
    public virtual string GetVersion() => "initial";
}
public interface ICommandContext
{
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
    ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
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
public interface ICommandWithHandler<TCommand, TProjector, TAggregatePayload>
    where TCommand : ICommandWithHandler<TCommand, TProjector, TAggregatePayload>
    where TProjector : IAggregateProjector, new()
    where TAggregatePayload : IAggregatePayload
{
    public PartitionKeys SpecifyPartitionKeys();
    public ResultBox<EventOrNone> Handle(ICommandContext<TAggregatePayload> context);
    public static abstract ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<TAggregatePayload> context);
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
    // CommandWithHandler 版 １クラスに定義できるが、実態は、メソッドを関数版に渡している
    public Task<ResultBox<CommandResponse>> Execute<TCommand>(TCommand command)
        where TCommand : ICommandWithHandlerCommon<TCommand>, IEquatable<TCommand> => Execute(
        command,
        command.GetProjector(),
        command.SpecifyPartitionKeys,
        command.Handle);

    public Task<ResultBox<CommandResponse>> Execute<TCommand, TAggregateProjector, TInject>(
        TCommand command,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        TInject inject,
        Func<TCommand, ICommandContext, TInject, ResultBox<EventOrNone>> handler) where TCommand : ICommand
        where TAggregateProjector : IAggregateProjector, new() =>
        throw new NotImplementedException();

    // Command 版 これを使えば、CommandHandler クラスは不要
    public Task<ResultBox<CommandResponse>> Execute<TCommand>(
        TCommand command,
        IAggregateProjector projector,
        Func<TCommand, PartitionKeys> specifyPartitionKeys,
        Func<TCommand, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand => ResultBox
        .Start
        .Conveyor(_ => specifyPartitionKeys(command).ToResultBox())
        .Combine(keys => Repository.Load(keys, projector))
        .Combine((partitionKeys, aggregate) => ResultBox.FromValue(new CommandContext(aggregate, projector)))
        .Combine(
            (partitionKeys, aggregate, context) =>
                RunCommand(command, context, handler)) // todo : runcommand がTaskの場合、ResultBox で失敗するのでResultBox 側のチェック必要
        .Conveyor(
            (partitionKeys, aggregate, context, executed) =>
                ResultBox.FromValue(new CommandResponse(partitionKeys, executed.Events, 0)))
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
        .Combine((partitionKeys, aggregate) => ResultBox.FromValue(new CommandContext(aggregate, projector)))
        .Combine(
            (partitionKeys, aggregate, context) =>
                RunCommand(
                    command,
                    inject,
                    context,
                    handler)) // todo : runcommand がTaskの場合、ResultBox で失敗するのでResultBox 側のチェック必要
        .Conveyor(
            (partitionKeys, aggregate, context, executed) =>
                ResultBox.FromValue(new CommandResponse(partitionKeys, executed.Events, 0)))
        .ToTask();


    public ResultBox<CommandExecuted> RunCommand<TCommand>(
        TCommand command,
        CommandContext context,
        Func<TCommand, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
        handler(command, context)
            .Conveyor(
                eventOrNone => ResultBox.FromValue(
                    new CommandExecuted(
                        context.Aggregate,
                        eventOrNone.HasEvent
                            ? context
                                .Events
                                .ToImmutableList()
                                .Add(new Event<IEventPayload>(eventOrNone.GetValue()))
                                .ToList()
                            : context.Events)));

    public ResultBox<CommandExecuted> RunCommand<TCommand, TInject>(
        TCommand command,
        TInject inject,
        CommandContext context,
        Func<TCommand, TInject, ICommandContext, ResultBox<EventOrNone>> handler) where TCommand : ICommand =>
        handler(command, inject, context)
            .Conveyor(
                eventOrNone => ResultBox.FromValue(
                    new CommandExecuted(
                        context.Aggregate,
                        eventOrNone.HasEvent
                            ? context
                                .Events
                                .ToImmutableList()
                                .Add(new Event<IEventPayload>(eventOrNone.GetValue()))
                                .ToList()
                            : context.Events)));

    public record CommandExecuted(IAggregate Aggregate, List<IEvent> Events);
}
public class CommandContext(IAggregate aggregate, IAggregateProjector projector) : ICommandContext
{
    public IAggregate Aggregate { get; set; } = aggregate;
    public IAggregateProjector Projector { get; } = projector;
    public List<IEvent> Events { get; } = new();
    public IAggregate GetAggregate() => throw new NotImplementedException();
    public EventOrNone AppendEvent(IEventPayload eventPayload)
    {
        var toAdd = new Event<IEventPayload>(eventPayload);
        var aggregatePayload = Projector.Project(Aggregate.GetPayload(), toAdd);
        Aggregate = new Aggregate<IAggregatePayload>(aggregatePayload);
        Events.Add(toAdd);
        return EventOrNone.Empty;
    }
}
public class Repository
{
    public Func<string, IEvent> Deserializer { get; set; } = s => throw new NotImplementedException();
    public Func<IEvent, string> Serializer { get; set; } = s => throw new NotImplementedException();
    public static ResultBox<IAggregate> Load<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() => Load(partitionKeys, new TAggregateProjector());
    public static ResultBox<IAggregate> Load(PartitionKeys partitionKeys, IAggregateProjector projector) =>
        new NotImplementedException();
}

// public interface IdelegateCommand<TCommand> where TCommand : ICommand
// {
//     public void declare(IDistributedSekibanApplication app);
// }
// public class delegateCommand : IdelegateCommand<A>
// {
//
//     public void declare(IDistributedSekibanApplication app) => app.MapCommand(
//         (A command, Func<Branch> getAggregateState) =>
//         {
//
//         });
//
//
//
// }
