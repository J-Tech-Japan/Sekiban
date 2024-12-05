using ResultBoxes;
using System.Reflection.Metadata;
namespace Sekiban.Pure;

public interface IEvent
{
    public IEventPayload GetPayload();
}
public record Event<TEventPayload>(TEventPayload Payload) : IEvent where TEventPayload : IEventPayload
{
    public IEventPayload GetPayload() => Payload;
} 
public interface IAggregatePayload { }
public record EmptyAggregatePayload : IAggregatePayload;
public record Aggregate<TAggregatePayload>(TAggregatePayload Payload) : IAggregate where TAggregatePayload : IAggregatePayload
{
    public IAggregatePayload GetPayload() => Payload;
}
public interface IAggregate
{
    public IAggregatePayload GetPayload();
}
public interface IEventPayload { }
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

public interface ICommand<TCommand, TProjector> where TCommand : ICommand<TCommand, TProjector>, IEquatable<TCommand>
 where TProjector : IAggregateProjector
{
    // インスタンス版 純粋関数っぽくないが、実際にはインスタンスがイミュータブルであれば問題なく、
    public PartitionKeys SpecifyPartitionKeys();
    public ResultBox<EventOrNone> Handle(ICommandContext context);
    
    // static 版 純粋関数に見えるが、実際にはインスタンスがイミュータブルかが重要
    public abstract static ResultBox<EventOrNone> Handle(TCommand command, ICommandContext context);
    public abstract static PartitionKeys SpecifyPartitionKeys(TCommand command);

    // 関数を返すけど、書き方が面倒になるため、あまりお勧めしない
    public Func<TCommand, ICommandContext, ResultBox<EventOrNone>> Handler { get; }
    public Func<TCommand, ICommandContext, ResultBox<EventOrNone>> Handler2();
}
public interface ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayload
{
    public Aggregate<TAggregatePayload> GetAggregate();
    public EventOrNone AppendEvent(IEventPayload eventPayload);
}
public interface ICommandWithHandler<TCommand, TProjector, TAggregatePayload> where TCommand : ICommandWithHandler<TCommand, TProjector, TAggregatePayload>
 where TProjector : IAggregateProjector
 where TAggregatePayload : IAggregatePayload
{
    public PartitionKeys SpecifyPartitionKeys();
    public ResultBox<EventOrNone> Handle(ICommandContext<TAggregatePayload> context);
    public abstract static ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<TAggregatePayload> context);

} 
public record PartitionKeys(Guid AggregateId, string Group, string RootPartitionKey)
{
    public const string DefaultRootPartitionKey = "default";
    public const string DefaultAggregateGroupName = "default";
    public static PartitionKeys Generate(string group = DefaultAggregateGroupName, string rootPartitionKey = DefaultRootPartitionKey) =>
        new(Guid.CreateVersion7(), group, rootPartitionKey);
    public static PartitionKeys Generate<TAggregateProjector>(string rootPartitionKey = DefaultRootPartitionKey) where TAggregateProjector : IAggregateProjector =>
        new(Guid.CreateVersion7(), typeof(TAggregateProjector).Name, rootPartitionKey);
    public static PartitionKeys Existing(
        Guid aggregateId,
        string group = "default",
        string rootPartitionKey = "default") =>
        new(aggregateId, group, rootPartitionKey);
    public static PartitionKeys Existing<TAggregateProjector>(
        Guid aggregateId,
        string rootPartitionKey = "default")  where TAggregateProjector : IAggregateProjector  =>
        new(aggregateId, typeof(TAggregateProjector).Name, rootPartitionKey);
}
public record TenantPartitionKeys(string TenantCode)
{
    public static TenantPartitionKeys Tenant(string tenantCode) => new(tenantCode);
    
    public PartitionKeys Generate(string group = PartitionKeys.DefaultAggregateGroupName) => PartitionKeys.Generate(group, TenantCode);
    public PartitionKeys Existing(Guid aggregateId, string group = PartitionKeys.DefaultAggregateGroupName) => PartitionKeys.Existing(aggregateId,group,TenantCode);
}
public record EventOrNone(IEventPayload? EventPayload, bool HasEvent)
{
    public static EventOrNone Empty => new(default, false);
    public static ResultBox<EventOrNone> None => Empty;
    public static EventOrNone FromValue(IEventPayload value) => new(value, true);
    public static ResultBox<EventOrNone> Event(IEventPayload value) => ResultBox.FromValue(FromValue(value));
    public IEventPayload GetValue() => HasEvent && EventPayload is not null
        ? EventPayload
        : throw new ResultsInvalidOperationException("no value");
    public static implicit operator EventOrNone(UnitValue value) => Empty;
}
