using MediatR;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
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
        where TSingleProjection : ISingleProjectionPayload
    {
        var singleProjectionType = typeof(TSingleProjection);
        if (!singleProjectionType.IsSingleProjectionType())
        {
            throw new ArgumentException($"Type {singleProjectionType} is not a single projection type");
        }
        if (singleProjectionType.GetOriginalTypeFromSingleProjection() != AggregateType)
        {
            throw new ArgumentException($"Single projection {singleProjectionType.Name} must be for aggregate {AggregateType.Name}");
        }
        SingleProjectionTypes = SingleProjectionTypes.Add(typeof(TSingleProjection));
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddAggregateQuery<TQuery>()
    {
        if (typeof(TQuery).IsAggregateQueryType())
        {
            AggregateQueryTypes = AggregateQueryTypes.Add(typeof(TQuery));
        }
        else
        {
            throw new ArgumentException("Type must implement IAggregateQuery", typeof(TQuery).Name);
        }
        return this;
    }
    public AggregateDependencyDefinition<TAggregatePayload> AddAggregateListQuery<TQuery>()
    {
        if (typeof(TQuery).IsAggregateListQueryType())
        {
            AggregateListQueryTypes = AggregateListQueryTypes.Add(typeof(TQuery));
        }
        else
        {
            throw new ArgumentException("Type must implement IAggregateListQuery", typeof(TQuery).Name);
        }
        return this;
    }
    public AggregateDependencyDefinition<TAggregatePayload> AddSingleProjectionQuery<TQuery>()
    {
        if (typeof(TQuery).IsSingleProjectionQueryType())
        {
            SingleProjectionQueryTypes = SingleProjectionQueryTypes.Add(typeof(TQuery));
        }
        else
        {
            throw new ArgumentException("Type must implement ISingleProjectionQuery", typeof(TQuery).Name);
        }
        return this;
    }
    public AggregateDependencyDefinition<TAggregatePayload> AddSingleProjectionListQuery<TQuery>()
    {
        if (typeof(TQuery).IsSingleProjectionListQueryType())
        {
            SingleProjectionListQueryTypes = SingleProjectionListQueryTypes.Add(typeof(TQuery));
        }
        else
        {
            throw new ArgumentException("Type must implement ISingleProjectionListQuery", typeof(TQuery).Name);
        }
        return this;
    }
}
