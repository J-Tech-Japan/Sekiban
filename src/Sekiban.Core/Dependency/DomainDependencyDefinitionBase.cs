using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using System.Collections.Immutable;
using System.Reflection;
namespace Sekiban.Core.Dependency;

public abstract class DomainDependencyDefinitionBase : IDependencyDefinition
{
    protected DomainDependencyDefinitionBase()
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        Define();
    }
    private ImmutableList<IAggregateDependencyDefinition> AggregateDefinitions { get; set; } = ImmutableList<IAggregateDependencyDefinition>.Empty;
    private ImmutableList<Type> MultiProjectionQueryFilterTypes { get; set; } = ImmutableList<Type>.Empty;
    private ImmutableList<Type> MultiProjectionListQueryFilterTypes { get; set; } = ImmutableList<Type>.Empty;
    public abstract Assembly GetExecutingAssembly();
    public IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies()
    {
        return AggregateDefinitions.SelectMany(s => s.CommandTypes);
    }
    public IEnumerable<(Type serviceType, Type? implementationType)> GetSubscriberDependencies()
    {
        return AggregateDefinitions.SelectMany(s => s.SubscriberTypes);
    }
    public IEnumerable<Type> GetAggregateQueryTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.AggregateQueryFilterTypes);
    }
    public IEnumerable<Type> GetAggregateListQueryTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.AggregateListQueryFilterTypes);
    }
    public IEnumerable<Type> GetSingleProjectionQueryTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.SingleProjectionQueryFilterTypes);
    }
    public IEnumerable<Type> GetSingleProjectionListQueryTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.SingleProjectionListQueryFilterTypes);
    }
    public IEnumerable<Type> GetMultiProjectionQueryTypes() => MultiProjectionQueryFilterTypes;
    public IEnumerable<Type> GetMultiProjectionListQueryTypes() => MultiProjectionListQueryFilterTypes;
    public IEnumerable<Type> GetSingleProjectionTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.SingleProjectionTypes);
    }
    protected abstract void Define();
    public IEnumerable<Type> GetAggregateTypes()
    {
        return AggregateDefinitions.Select(s => s.AggregateType);
    }
    protected AggregateDependencyDefinition<TAggregatePayload> AddAggregate<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload, new()
    {
        if (AggregateDefinitions.SingleOrDefault(s => s.AggregateType == typeof(TAggregatePayload)) is
            AggregateDependencyDefinition<TAggregatePayload> existing)
        {
            return existing;
        }

        var newone = new AggregateDependencyDefinition<TAggregatePayload>();
        AggregateDefinitions = AggregateDefinitions.Add(newone);
        return newone;
    }
    protected void AddMultiProjectionQuery<TQueryFilter>()
    {
        var t = typeof(TQueryFilter);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType)
                .Select(s => s.GetGenericTypeDefinition()) switch
            {
                { } gis when gis.Contains(typeof(IMultiProjectionQuery<,,,>)) => () =>
                    MultiProjectionQueryFilterTypes = MultiProjectionQueryFilterTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
    }
    protected void AddMultiProjectionListQuery<TQueryFilter>()
    {
        var t = typeof(TQueryFilter);
        Action action = t.GetInterfaces()
                .Where(w => w.IsGenericType)
                .Select(s => s.GetGenericTypeDefinition()) switch
            {
                { } gis when gis.Contains(typeof(IMultiProjectionListQuery<,,,>)) => () =>
                    MultiProjectionListQueryFilterTypes = MultiProjectionListQueryFilterTypes.Add(t),
                _ => throw new NotImplementedException()
            };
        action();
    }
}
