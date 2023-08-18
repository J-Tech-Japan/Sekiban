using MediatR;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

public class
    AggregateSubtypeDependencyDefinition<TParentAggregatePayload, TAggregateSubtypePayload> : IAggregateSubTypeDependencyDefinition<
        TParentAggregatePayload> where TParentAggregatePayload : IAggregatePayloadCommon where TAggregateSubtypePayload : IAggregatePayloadCommon
{
    public AggregateDependencyDefinition<TParentAggregatePayload> ParentAggregateDependencyDefinition { get; init; }
    internal AggregateSubtypeDependencyDefinition(AggregateDependencyDefinition<TParentAggregatePayload> parentAggregateDependencyDefinition) =>
        ParentAggregateDependencyDefinition = parentAggregateDependencyDefinition;
    public ImmutableList<(Type, Type?)> SubscriberTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;
    public ImmutableList<(Type, Type?)> CommandTypes { get; private set; } = ImmutableList<(Type, Type?)>.Empty;
    public AggregateDependencyDefinition<TParentAggregatePayload> GetParentAggregateDependencyDefinition() => ParentAggregateDependencyDefinition;
    public AggregateSubtypeDependencyDefinition<TParentAggregatePayload, TAggregateSubtypePayload> AddEventSubscriber<TEvent, TEventSubscriber>()
        where TEvent : IEventPayloadApplicableTo<TAggregateSubtypePayload> where TEventSubscriber : IEventSubscriber<TEvent, TEventSubscriber>
    {
        SubscriberTypes = SubscriberTypes.Add((typeof(INotificationHandler<Event<TEvent>>), typeof(EventSubscriber<TEvent, TEventSubscriber>)));
        SubscriberTypes = SubscriberTypes.Add((typeof(IEventSubscriber<TEvent, TEventSubscriber>), typeof(TEventSubscriber)));
        return this;
    }
    public AggregateSubtypeDependencyDefinition<TParentAggregatePayload, TAggregateSubtypePayload>
        AddCommandHandler<TCreateCommand, TCommandHandler>() where TCreateCommand : ICommand<TAggregateSubtypePayload>
        where TCommandHandler : ICommandHandlerCommon<TAggregateSubtypePayload, TCreateCommand>
    {
        CommandTypes = CommandTypes.Add((typeof(ICommandHandlerCommon<TAggregateSubtypePayload, TCreateCommand>), typeof(TCommandHandler)));
        return this;
    }
}
