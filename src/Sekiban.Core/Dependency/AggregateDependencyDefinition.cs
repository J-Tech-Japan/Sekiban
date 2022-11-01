using MediatR;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

public class AggregateDependencyDefinition<TAggregatePayload> : IAggregateDependencyDefinition
    where TAggregatePayload : IAggregatePayload, new()
{

    public AggregateDependencyDefinition() => AggregateType = typeof(TAggregatePayload);
    public ImmutableList<(Type, Type?)> CommandTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;
    public ImmutableList<(Type, Type?)> SubscriberTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;
    public ImmutableList<Type> AggregateQueryFilterTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> AggregateListQueryFilterTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionQueryFilterTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionListQueryFilterTypes { get; private set; } = ImmutableList<Type>.Empty;
    public Type AggregateType { get; }

    public AggregateDependencyDefinition<TAggregatePayload> AddCreateCommandHandler<TCreateCommand, TCommandHandler>()
        where TCreateCommand : ICreateCommand<TAggregatePayload>, new()
        where TCommandHandler : CreateCommandHandlerBase<TAggregatePayload, TCreateCommand>
    {
        CommandTypes = CommandTypes.Add((typeof(ICreateCommandHandler<TAggregatePayload, TCreateCommand>), typeof(TCommandHandler)));
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddChangeCommandHandler<TChangeCommand, TCommandHandler>()
        where TChangeCommand : ChangeCommandBase<TAggregatePayload>, new()
        where TCommandHandler : IChangeCommandHandler<TAggregatePayload, TChangeCommand>
    {
        CommandTypes = CommandTypes.Add((typeof(IChangeCommandHandler<TAggregatePayload, TChangeCommand>), typeof(TCommandHandler)));
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddEventSubscriber<TEvent, TEventSubscriber>()
        where TEvent : IApplicableEvent<TAggregatePayload>
        where TEventSubscriber : EventSubscriberBase<TEvent>
    {
        SubscriberTypes = SubscriberTypes.Add((typeof(INotificationHandler<Event<TEvent>>), typeof(TEventSubscriber)));
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddSingleProjection<TSingleProjection>()
        where TSingleProjection : ISingleProjection
    {
        var singleProjectionType = typeof(TSingleProjection);
        var singleProjectionBase = singleProjectionType.BaseType;
        if (singleProjectionBase is null ||
            !singleProjectionBase.IsGenericType ||
            singleProjectionBase.GetGenericTypeDefinition() != typeof(SingleProjectionBase<,,>))
        {
            throw new ArgumentException($"Single projection {singleProjectionType.Name} must inherit from SingleProjectionBase<,,>");
        }
        if (singleProjectionBase.GenericTypeArguments[0] != AggregateType)
        {
            throw new ArgumentException($"Single projection {singleProjectionType.Name} must be for aggregate {AggregateType.Name}");
        }
        SingleProjectionTypes = SingleProjectionTypes.Add(typeof(TSingleProjection));
        return this;
    }
    public AggregateDependencyDefinition<TAggregatePayload> AddQuery<TQueryFilter>()
    {
        var t = typeof(TQueryFilter);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType && w.GenericTypeArguments.Contains(typeof(TAggregatePayload)))
                .Select(s => s.GetGenericTypeDefinition())
                .ToArray() switch
            {
                [..] i when i.Contains(typeof(IAggregateQuery<,,>)) => () =>
                    AggregateQueryFilterTypes = AggregateQueryFilterTypes.Add(t),
                [..] i when i.Contains(typeof(IAggregateListQuery<,,>)) => () =>
                    AggregateListQueryFilterTypes = AggregateListQueryFilterTypes.Add(t),
                [..] i when i.Contains(typeof(ISingleProjectionQuery<,,,,>)) => () =>
                    SingleProjectionQueryFilterTypes = SingleProjectionQueryFilterTypes.Add(t),
                [..] i when i.Contains(typeof(ISingleProjectionListQuery<,,,,>)) => () =>
                    SingleProjectionListQueryFilterTypes = SingleProjectionListQueryFilterTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
        return this;
    }
}
