using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using System.Reflection;
namespace Sekiban.Core.Dependency;

/// <summary>
///     System use base interface for Dependency Definition
///     Application developers does not need to use this interface directly
/// </summary>
public interface IDependencyDefinition : IQueryDefinition
{
    public SekibanDependencyOptions GetSekibanDependencyOptions() =>
        new(
            new RegisteredEventTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            new SekibanAggregateTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            GetCommandDependencies().Concat(GetSubscriberDependencies()));

    Assembly GetExecutingAssembly();
    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
    IEnumerable<(Type serviceType, Type? implementationType)> GetSubscriberDependencies();
    IEnumerable<Action<IServiceCollection>> GetServiceActions();
    IEnumerable<IAggregateDependencyDefinition> GetAggregateDefinitions();
    void Define();
}
