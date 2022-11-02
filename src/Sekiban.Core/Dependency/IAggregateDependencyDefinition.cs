using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

public interface IAggregateDependencyDefinition
{
    Type AggregateType { get; }
    ImmutableList<(Type, Type?)> CommandTypes { get; }
    ImmutableList<(Type, Type?)> SubscriberTypes { get; }
    ImmutableList<Type> AggregateQueryFilterTypes { get; }
    ImmutableList<Type> AggregateListQueryFilterTypes { get; }
    ImmutableList<Type> SingleProjectionTypes { get; }
    ImmutableList<Type> SingleProjectionQueryFilterTypes { get; }
    ImmutableList<Type> SingleProjectionListQueryFilterTypes { get; }
}
