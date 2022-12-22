using System.Reflection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;

namespace Sekiban.Core.Dependency;

public interface IDependencyDefinition : IQueryDefinition
{
    public SekibanDependencyOptions GetSekibanDependencyOptions()
    {
        return new(
            new RegisteredEventTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            new SekibanAggregateTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            GetCommandDependencies().Concat(GetSubscriberDependencies()));
    }

    Assembly GetExecutingAssembly();
    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
    IEnumerable<(Type serviceType, Type? implementationType)> GetSubscriberDependencies();
}
