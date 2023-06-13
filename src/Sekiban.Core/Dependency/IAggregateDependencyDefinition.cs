using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

// ReSharper disable once InvalidXmlDocComment
/// <summary>
///     system use base class for <see cref="AggregateDependencyDefinition" />
///     Application developer does not need to use this class directly.
/// </summary>
public interface IAggregateDependencyDefinition
{
    Type AggregateType { get; }
    ImmutableList<Type> AggregateSubtypes { get; }
    ImmutableList<(Type, Type?)> CommandTypes { get; }
    ImmutableList<(Type, Type?)> SubscriberTypes { get; }
    ImmutableList<Type> AggregateQueryTypes { get; }
    ImmutableList<Type> AggregateListQueryTypes { get; }
    ImmutableList<Type> SingleProjectionTypes { get; }
    ImmutableList<Type> SingleProjectionQueryTypes { get; }
    ImmutableList<Type> SingleProjectionListQueryTypes { get; }
}
