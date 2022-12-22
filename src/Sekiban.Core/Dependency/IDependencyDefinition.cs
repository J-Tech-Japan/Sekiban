using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using System.Reflection;
namespace Sekiban.Core.Dependency;

public interface IDependencyDefinition : IQueryDefinition
{
    public SekibanDependencyOptions GetSekibanDependencyOptions() => new SekibanDependencyOptions(
        new RegisteredEventTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
        new SekibanAggregateTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
        GetCommandDependencies().Concat(GetSubscriberDependencies()));

    Assembly GetExecutingAssembly();
    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
    IEnumerable<(Type serviceType, Type? implementationType)> GetSubscriberDependencies();
}
