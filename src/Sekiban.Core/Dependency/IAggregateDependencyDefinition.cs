using Sekiban.Core.Query.QueryModel;
using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

// ReSharper disable once InvalidXmlDocComment
/// <summary>
///     system use base class for <see cref="AggregateDependencyDefinition" />
///     Application developer does not need to use this class directly.
/// </summary>
public interface IAggregateDependencyDefinition
{
    /// <summary>
    ///     Aggregate Payload Type, only put parent type if it uses subtypes
    /// </summary>
    Type AggregatePayloadType { get; }

    /// <summary>
    /// </summary>
    ImmutableList<Type> AggregatePayloadSubtypes { get; }
    /// <summary>
    ///     Command Types
    /// </summary>
    ImmutableList<(Type, Type?)> CommandTypes { get; }
    /// <summary>
    ///     Event Subscriber Types
    /// </summary>
    ImmutableList<(Type, Type?)> SubscriberTypes { get; }
    /// <summary>
    ///     Queries that uses Aggregate List and return single object
    ///     should inherit from <see cref="IAggregateQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    ImmutableList<Type> AggregateQueryTypes { get; }
    /// <summary>
    ///     Queries that uses Aggregate List and return list object
    ///     should inherit from <see cref="IAggregateListQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    ImmutableList<Type> AggregateListQueryTypes { get; }
    /// <summary>
    ///     List of Single Projection Types
    /// </summary>
    ImmutableList<Type> SingleProjectionTypes { get; }
    /// <summary>
    ///     Queries that uses Single Projection List and return single object
    ///     should inherit from <see cref="ISingleProjectionQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    ImmutableList<Type> SingleProjectionQueryTypes { get; }
    /// <summary>
    ///     Queries that uses Aggregate List and return list object
    ///     should inherit from <see cref="ISingleProjectionListQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    ImmutableList<Type> SingleProjectionListQueryTypes { get; }
}
