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
public class AggregateDependencyDefinition<TAggregatePayload> : IAggregateDependencyDefinition where TAggregatePayload : IAggregatePayloadCommon
{
    /// <summary>
    ///     Subtypes of this aggregate, edit only from method AddSubType
    /// </summary>
    public ImmutableList<IAggregateSubTypeDependencyDefinition<TAggregatePayload>> SubAggregates { get; private set; }
        = ImmutableList<IAggregateSubTypeDependencyDefinition<TAggregatePayload>>.Empty;

    protected ImmutableList<(Type, Type?)> SelfCommandTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;
    protected ImmutableList<(Type, Type?)> SelfSubscriberTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;

    /// <summary>
    ///     Get Only Aggregate Type
    /// </summary>
    public AggregateDependencyDefinition() => AggregateType = typeof(TAggregatePayload);

    public ImmutableList<Type> AggregateSubtypes => SubAggregates.Select(m => m.GetType().GetGenericArguments().Last()).ToImmutableList();
    /// <summary>
    ///     Get Aggregate commands
    /// </summary>
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
    /// <summary>
    ///     Aggregate Event Subscribers
    /// </summary>
    public ImmutableList<(Type, Type?)> SubscriberTypes
    {
        get
        {
            var types = SelfSubscriberTypes;
            foreach (var subAggregate in SubAggregates)
            {
                types = types.AddRange(subAggregate.SubscriberTypes);
            }
            return types;
        }
    }
    /// <summary>
    ///     Aggregate Query Types
    /// </summary>
    public ImmutableList<Type> AggregateQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    /// <summary>
    ///     Aggregate List Query Types
    /// </summary>
    public ImmutableList<Type> AggregateListQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    /// <summary>
    ///     Aggregate Single Projection Types
    /// </summary>
    public ImmutableList<Type> SingleProjectionTypes { get; private set; } = ImmutableList<Type>.Empty;
    /// <summary>
    ///     Aggregate Single Projection Query Types
    /// </summary>
    public ImmutableList<Type> SingleProjectionQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    /// <summary>
    ///     Aggregate Single Projection List Query Types
    /// </summary>
    public ImmutableList<Type> SingleProjectionListQueryTypes { get; private set; } = ImmutableList<Type>.Empty;
    /// <summary>
    ///     Aggregate Type
    /// </summary>
    public Type AggregateType { get; }
    /// <summary>
    ///     Add Command Handler to Aggregate
    /// </summary>
    /// <typeparam name="TCreateCommand">Target Command</typeparam>
    /// <typeparam name="TCommandHandler">Command Handler for Target Command</typeparam>
    /// <returns>Self for method chain</returns>
    public AggregateDependencyDefinition<TAggregatePayload> AddCommandHandler<TCreateCommand, TCommandHandler>()
        where TCreateCommand : ICommand<TAggregatePayload>, new() where TCommandHandler : ICommandHandlerCommon<TAggregatePayload, TCreateCommand>
    {
        SelfCommandTypes = SelfCommandTypes.Add((typeof(ICommandHandlerCommon<TAggregatePayload, TCreateCommand>), typeof(TCommandHandler)));
        return this;
    }
    /// <summary>
    ///     Add Event Subscriber to Aggregate Event
    /// </summary>
    /// <typeparam name="TEvent">
    ///     Target Event
    /// </typeparam>
    /// <typeparam name="TEventSubscriber">
    ///     Subscriber for Target Event
    /// </typeparam>
    /// <returns>
    ///     Self for method chain
    /// </returns>
    public AggregateDependencyDefinition<TAggregatePayload> AddEventSubscriber<TEvent, TEventSubscriber>()
        where TEvent : IEventPayloadApplicableTo<TAggregatePayload> where TEventSubscriber : IEventSubscriber<TEvent>
    {
        SelfSubscriberTypes
            = SelfSubscriberTypes.Add((typeof(INotificationHandler<Event<TEvent>>), typeof(EventSubscriber<TEvent, TEventSubscriber>)));
        SelfSubscriberTypes = SelfSubscriberTypes.Add((typeof(IEventSubscriber<TEvent>), typeof(TEventSubscriber)));
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
        } else
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
        } else
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
        } else
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
        } else
        {
            throw new ArgumentException("Type must implement ISingleProjectionListQuery", typeof(TQuery).Name);
        }
        return this;
    }

    public AggregateDependencyDefinition<TAggregatePayload> AddSubtype<TSubAggregatePayload>(
        Action<AggregateSubtypeDependencyDefinition<TAggregatePayload, TSubAggregatePayload>> subAggregateDefinitionAction)
        where TSubAggregatePayload : TAggregatePayload
    {
        var subAggregate = new AggregateSubtypeDependencyDefinition<TAggregatePayload, TSubAggregatePayload>(this);
        SubAggregates = SubAggregates.Add(subAggregate);
        subAggregateDefinitionAction(subAggregate);
        return this;
    }
}
