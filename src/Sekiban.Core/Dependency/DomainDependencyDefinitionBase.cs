using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Types;
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
    private ImmutableList<Type> MultiProjectionQueryTypes { get; set; } = ImmutableList<Type>.Empty;
    private ImmutableList<Type> MultiProjectionListQueryTypes { get; set; } = ImmutableList<Type>.Empty;
    private ImmutableList<Assembly> Assemblies { get; set; } = ImmutableList<Assembly>.Empty;
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
        return AggregateDefinitions.SelectMany(s => s.AggregateQueryTypes);
    }
    public IEnumerable<Type> GetAggregateListQueryTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.AggregateListQueryTypes);
    }
    public IEnumerable<Type> GetSingleProjectionQueryTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.SingleProjectionQueryTypes);
    }
    public IEnumerable<Type> GetSingleProjectionListQueryTypes()
    {
        return AggregateDefinitions.SelectMany(s => s.SingleProjectionListQueryTypes);
    }
    public IEnumerable<Type> GetMultiProjectionQueryTypes() => MultiProjectionQueryTypes;
    public IEnumerable<Type> GetMultiProjectionListQueryTypes() => MultiProjectionListQueryTypes;

    public virtual SekibanDependencyOptions GetSekibanDependencyOptions() => new(
        new RegisteredEventTypes(GetAssembliesForOptions()),
        new SekibanAggregateTypes(GetAssembliesForOptions()),
        GetCommandDependencies().Concat(GetSubscriberDependencies()));
    public DomainDependencyDefinitionBase AddDependency<TDependency>() where TDependency : DomainDependencyDefinitionBase, new()
    {
        var toAdd = new TDependency();
        AggregateDefinitions = AggregateDefinitions.Concat(toAdd.AggregateDefinitions).ToImmutableList();
        MultiProjectionQueryTypes = MultiProjectionQueryTypes.Concat(toAdd.MultiProjectionQueryTypes).ToImmutableList();
        MultiProjectionListQueryTypes = MultiProjectionListQueryTypes.Concat(toAdd.MultiProjectionListQueryTypes).ToImmutableList();
        Assemblies = Assemblies.Concat(toAdd.Assemblies).ToImmutableList();
        return this;
    }
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
    protected void AddMultiProjectionQuery<TQuery>()
    {
        if (typeof(TQuery).IsMultiProjectionQueryType())
        {
            MultiProjectionQueryTypes = MultiProjectionQueryTypes.Add(typeof(TQuery));
        }
        else
        {
            throw new ArgumentException("Type must implement MultiProjectionQuery", typeof(TQuery).Name);
        }
    }
    protected void AddMultiProjectionListQuery<TQuery>()
    {
        if (typeof(TQuery).IsMultiProjectionListQueryType())
        {
            MultiProjectionListQueryTypes = MultiProjectionListQueryTypes.Add(typeof(TQuery));
        }
        else
        {
            throw new ArgumentException("Type must implement MultiProjectionListQuery", typeof(TQuery).Name);
        }
    }

    private Assembly[] GetAssembliesForOptions() =>
        Assemblies.Add(GetExecutingAssembly()).Add(SekibanEventSourcingDependency.GetAssembly()).ToArray();
}
