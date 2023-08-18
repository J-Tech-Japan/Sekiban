using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Types;
using System.Collections.Immutable;
using System.Reflection;
namespace Sekiban.Core.Dependency;

/// <summary>
///     Defines a sekiban dependency for the each project.
///     Application developers can inherit this class and override the Define() method to define your dependencies.
/// </summary>
public abstract class DomainDependencyDefinitionBase : IDependencyDefinition
{

    private ImmutableList<IAggregateDependencyDefinition> AggregateDefinitions { get; set; } = ImmutableList<IAggregateDependencyDefinition>.Empty;

    private ImmutableList<Type> MultiProjectionQueryTypes { get; set; } = ImmutableList<Type>.Empty;
    private ImmutableList<Type> MultiProjectionListQueryTypes { get; set; } = ImmutableList<Type>.Empty;
    private ImmutableList<Type> GeneralQueryTypes { get; set; } = ImmutableList<Type>.Empty;
    private ImmutableList<Type> GeneralListQueryTypes { get; set; } = ImmutableList<Type>.Empty;
    private ImmutableList<Assembly> Assemblies { get; set; } = ImmutableList<Assembly>.Empty;
    private ImmutableList<Action<IServiceCollection>> ServiceActions { get; set; } = ImmutableList<Action<IServiceCollection>>.Empty;
    public abstract Assembly GetExecutingAssembly();

    public IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies()
    {
        return AggregateDefinitions.SelectMany(s => s.CommandTypes);
    }

    public IEnumerable<Action<IServiceCollection>> GetServiceActions() => ServiceActions;
    public IEnumerable<IAggregateDependencyDefinition> GetAggregateDefinitions() => AggregateDefinitions;

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
    public IEnumerable<Type> GetGeneralQueryTypes() => GeneralQueryTypes;
    public IEnumerable<Type> GetGeneralListQueryTypes() => GeneralListQueryTypes;

    public virtual SekibanDependencyOptions GetSekibanDependencyOptions() =>
        new(
            new RegisteredEventTypes(GetAssembliesForOptions()),
            new SekibanAggregateTypes(GetAssembliesForOptions()),
            GetCommandDependencies().Concat(GetSubscriberDependencies()));

    public abstract void Define();


    public void AddServices(Action<IServiceCollection> serviceAction)
    {
        ServiceActions = ServiceActions.Add(serviceAction);
    }

    public DomainDependencyDefinitionBase AddDependency<TDependency>() where TDependency : DomainDependencyDefinitionBase, new()
    {
        var toAdd = new TDependency();
        toAdd.Define();
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

    public IEnumerable<Type> GetAggregatePayloadTypes()
    {
        return AggregateDefinitions.Select(s => s.AggregateType);
    }
    public IEnumerable<Type> GetAggregatePayloadSubtypes()
    {
        return AggregateDefinitions.SelectMany(s => s.AggregateSubtypes);
    }

    protected AggregateDependencyDefinition<TAggregatePayload> AddAggregate<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
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
        } else
        {
            throw new ArgumentException("Type must implement MultiProjectionQuery", typeof(TQuery).Name);
        }
    }

    protected void AddMultiProjectionListQuery<TQuery>()
    {
        if (typeof(TQuery).IsMultiProjectionListQueryType())
        {
            MultiProjectionListQueryTypes = MultiProjectionListQueryTypes.Add(typeof(TQuery));
        } else
        {
            throw new ArgumentException("Type must implement MultiProjectionListQuery", typeof(TQuery).Name);
        }
    }

    protected void AddGeneralQuery<TQuery>()
    {
        if (typeof(TQuery).IsGeneralQueryType())
        {
            GeneralQueryTypes = GeneralQueryTypes.Add(typeof(TQuery));
        } else
        {
            throw new ArgumentException("Type must implement GeneralQuery", typeof(TQuery).Name);
        }
    }

    protected void AddGeneralListQuery<TQuery>()
    {
        if (typeof(TQuery).IsGeneralListQueryType())
        {
            GeneralListQueryTypes = GeneralListQueryTypes.Add(typeof(TQuery));
        } else
        {
            throw new ArgumentException("Type must implement GeneralListQuery", typeof(TQuery).Name);
        }
    }


    private Assembly[] GetAssembliesForOptions() =>
        Assemblies.Add(GetExecutingAssembly()).Add(SekibanEventSourcingDependency.GetAssembly()).ToArray();
}
