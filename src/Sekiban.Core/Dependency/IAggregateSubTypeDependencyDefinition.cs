using Sekiban.Core.Aggregate;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

/// <summary>
///     Aggregate Subtype Dependency Definition
/// </summary>
/// <typeparam name="TParentAggregatePayload"></typeparam>
public interface IAggregateSubTypeDependencyDefinition<TParentAggregatePayload> where TParentAggregatePayload : IAggregatePayloadCommon
{
    /// <summary>
    ///     Subtype Command Types
    /// </summary>
    public ImmutableList<(Type, Type?)> CommandTypes { get; }
    /// <summary>
    ///     Subtype Event Subscriber Types
    /// </summary>
    public ImmutableList<(Type, Type?)> SubscriberTypes { get; }
    /// <summary>
    ///     Link to parent aggregate dependency definition
    /// </summary>
    /// <returns></returns>
    public AggregateDependencyDefinition<TParentAggregatePayload> GetParentAggregateDependencyDefinition();
}
