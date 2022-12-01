using System.Reflection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;

namespace Sekiban.Core.Dependency;

public interface IDependencyDefinition
{
    public SekibanDependencyOptions GetSekibanDependencyOptions()
    {
        return new(
            new RegisteredEventTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            new SekibanAggregateTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            GetCommandDependencies().Concat(GetSubscriberDependencies()));
    }

    Assembly GetExecutingAssembly();
    public IEnumerable<Type> GetAggregateListQueryTypes();
    public IEnumerable<Type> GetAggregateQueryTypes();
    public IEnumerable<Type> GetSingleProjectionListQueryTypes();
    public IEnumerable<Type> GetSingleProjectionQueryTypes();
    public IEnumerable<Type> GetMultiProjectionQueryTypes();
    public IEnumerable<Type> GetMultiProjectionListQueryTypes();
    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
    IEnumerable<(Type serviceType, Type? implementationType)> GetSubscriberDependencies();
}
