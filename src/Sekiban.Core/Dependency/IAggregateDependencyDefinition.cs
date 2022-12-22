using System.Collections.Immutable;
namespace Sekiban.Core.Dependency;

public interface IAggregateDependencyDefinition
{
    Type AggregateType { get; }
    ImmutableList<(Type, Type?)> CommandTypes { get; }
    ImmutableList<(Type, Type?)> SubscriberTypes { get; }
    ImmutableList<Type> AggregateQueryTypes { get; }
    ImmutableList<Type> AggregateListQueryTypes { get; }
    ImmutableList<Type> SingleProjectionTypes { get; }
    ImmutableList<Type> SingleProjectionQueryTypes { get; }
    ImmutableList<Type> SingleProjectionListQueryTypes { get; }
}
