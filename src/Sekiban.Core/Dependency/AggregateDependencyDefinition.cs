using MediatR;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

/// <summary>
///     A dependency container for the Aggregate Payload
///     Application developer will use this with <see cref="DomainDependencyDefinitionBase" />
///     AddAggregate
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public class AggregateDependencyDefinition<TAggregatePayload> : IAggregateDependencyDefinition
    where TAggregatePayload : IAggregatePayloadCommon
{

    public AggregateDependencyDefinition()
    {
        AggregateType = typeof(TAggregatePayload);
    }
    public ImmutableList<IAggregateSubTypeDependencyDefinition<TAggregatePayload>> SubAggregates { get; protected set; } =
        ImmutableList<IAggregateSubTypeDependencyDefinition<TAggregatePayload>>.Empty;

    protected ImmutableList<(Type, Type?)> SelfCommandTypes { get; set; } = ImmutableList<(Type, Type?)>.Empty;

    public ImmutableList<Type> AggregateSubtypes => SubAggregates.Select(m => m.GetType().GetGenericArguments().Last()).ToImmutableList();
    public virtual ImmutableList<(Type, Type?)> CommandTypes
    {
        get
        {
            var types = SelfCommandTypes;
            foreach (var subAggregate in SubAggregates)
            {
                types = types.AddRange(subAggregate.CommandTypes);
            }
            return types;
        }
    }
    public ImmutableList<(Type, Type?)> SubscriberTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;
    public ImmutableList<Type> AggregateQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> AggregateListQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    public ImmutableList<Type> SingleProjectionListQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    public Type AggregateType { get; }

    public AggregateDependencyDefinition<TAggregatePayload> AddCommandHandler<TCreateCommand, TCommandHandler>()
        where TCreateCommand : ICommand<TAggregatePayload>, new()
        where TCommandHandler : ICommandHandlerCommon<TAggregatePayload, TCreateCommand>
    {
        SelfCommandTypes = SelfCommandTypes.Add(
            (typeof(ICommandHandlerCommon<TAggregatePayload, TCreateCommand>),
                typeof(TCommandHandler)));
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddEventSubscriber<TEvent, TEventSubscriber>()
        where TEvent : IEventPayloadApplicableTo<TAggregatePayload>
        where TEventSubscriber : IEventSubscriber<TEvent>
    {
        SubscriberTypes = SubscriberTypes.Add((typeof(INotificationHandler<Event<TEvent>>), typeof(EventSubscriber<TEvent, TEventSubscriber>)));
        SubscriberTypes = SubscriberTypes.Add((typeof(IEventSubscriber<TEvent>), typeof(TEventSubscriber)));
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddSingleProjection<TSingleProjection>()
        where TSingleProjection : ISingleProjectionPayloadCommon
    {
        var singleProjectionType = typeof(TSingleProjection);
        if (!singleProjectionType.IsSingleProjectionPayloadType())
        {
            throw new ArgumentException($"Type {singleProjectionType} is not a single projection type");
        }
        if (singleProjectionType.GetOriginalTypeFromSingleProjectionPayload() != AggregateType)
        {
            throw new ArgumentException(
                $"Single projection {singleProjectionType.Name} must be for aggregate {AggregateType.Name}");
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

    public AggregateDependencyDefinition<TAggregatePayload> AddSubAggregate<TSubAggregatePayload>(
        Action<AggregateSubtypeDependencyDefinition<TAggregatePayload, TSubAggregatePayload>> subAggregateDefinitionAction)
        where TSubAggregatePayload : TAggregatePayload
    {
        var subAggregate = new AggregateSubtypeDependencyDefinition<TAggregatePayload, TSubAggregatePayload>(this);
        SubAggregates = SubAggregates.Add(subAggregate);
        subAggregateDefinitionAction(subAggregate);
        return this;
    }
}
