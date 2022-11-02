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
    public ImmutableList<Type> AggregateQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> AggregateListQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionListQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
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
            singleProjectionBase.GetGenericTypeDefinition() != typeof(MultiProjectionBase<,,>))
        {
            throw new ArgumentException($"Single projection {singleProjectionType.Name} must inherit from MultiProjectionBase<,,>");
        }
        if (singleProjectionBase.GenericTypeArguments[0] != AggregateType)
        {
            throw new ArgumentException($"Single projection {singleProjectionType.Name} must be for aggregateIdentifier {AggregateType.Name}");
        }
        SingleProjectionTypes = SingleProjectionTypes.Add(typeof(TSingleProjection));
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddAggregateQuery<TQuery>()
    {
        var t = typeof(TQuery);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType && w.GenericTypeArguments.Contains(typeof(TAggregatePayload)))
                .Select(s => s.GetGenericTypeDefinition())
                .ToArray() switch
            {
                [..] i when i.Contains(typeof(IAggregateQuery<,,>)) => () =>
                    AggregateQueryTypes = AggregateQueryTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
        return this;
    }
    public AggregateDependencyDefinition<TAggregatePayload> AddAggregateListQuery<TQuery>()
    {
        var t = typeof(TQuery);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType && w.GenericTypeArguments.Contains(typeof(TAggregatePayload)))
                .Select(s => s.GetGenericTypeDefinition())
                .ToArray() switch
            {
                [..] i when i.Contains(typeof(IAggregateListQuery<,,>)) => () =>
                    AggregateListQueryTypes = AggregateListQueryTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
        return this;
    }
    public AggregateDependencyDefinition<TAggregatePayload> AddSingleProjectionQuery<TQuery>()
    {
        var t = typeof(TQuery);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType && w.GenericTypeArguments.Contains(typeof(TAggregatePayload)))
                .Select(s => s.GetGenericTypeDefinition())
                .ToArray() switch
            {
                [..] i when i.Contains(typeof(ISingleProjectionQuery<,,,,>)) => () =>
                    SingleProjectionQueryTypes = SingleProjectionQueryTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
        return this;
    }
    public AggregateDependencyDefinition<TAggregatePayload> AddSingleProjectionListQuery<TQuery>()
    {
        var t = typeof(TQuery);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType && w.GenericTypeArguments.Contains(typeof(TAggregatePayload)))
                .Select(s => s.GetGenericTypeDefinition())
                .ToArray() switch
            {
                [..] i when i.Contains(typeof(ISingleProjectionListQuery<,,,,>)) => () =>
                    SingleProjectionListQueryTypes = SingleProjectionListQueryTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddQuery<TQuery>()
    {
        var t = typeof(TQuery);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType && w.GenericTypeArguments.Contains(typeof(TAggregatePayload)))
                .Select(s => s.GetGenericTypeDefinition())
                .ToArray() switch
            {
                [..] i when i.Contains(typeof(IAggregateQuery<,,>)) => () =>
                    AggregateQueryTypes = AggregateQueryTypes.Add(t),
                [..] i when i.Contains(typeof(IAggregateListQuery<,,>)) => () =>
                    AggregateListQueryTypes = AggregateListQueryTypes.Add(t),
                [..] i when i.Contains(typeof(ISingleProjectionQuery<,,,,>)) => () =>
                    SingleProjectionQueryTypes = SingleProjectionQueryTypes.Add(t),
                [..] i when i.Contains(typeof(ISingleProjectionListQuery<,,,,>)) => () =>
                    SingleProjectionListQueryTypes = SingleProjectionListQueryTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
        return this;
    }
}
