using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using System.Reflection;
namespace Sekiban.Core.Dependency;

public interface IDependencyDefinition
{
    public virtual SekibanDependencyOptions GetSekibanDependencyOptions()
    {
        return new SekibanDependencyOptions(
            new RegisteredEventTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            new SekibanAggregateTypes(GetExecutingAssembly(), SekibanEventSourcingDependency.GetAssembly()),
            GetCommandDependencies());
    }
    Assembly GetExecutingAssembly();
    public virtual IEnumerable<Type> GetAggregateListQueryFilterTypes() { return Enumerable.Empty<Type>(); }
    public virtual IEnumerable<Type> GetAggregateQueryFilterTypes() { return Enumerable.Empty<Type>(); }
    public virtual IEnumerable<Type> GetSingleAggregateProjectionListQueryFilterTypes() { return Enumerable.Empty<Type>(); }
    public virtual IEnumerable<Type> GetSingleAggregateProjectionQueryFilterTypes() { return Enumerable.Empty<Type>(); }
    public virtual IEnumerable<Type> GetProjectionQueryFilterTypes() { return Enumerable.Empty<Type>(); }
    public virtual IEnumerable<Type> GetProjectionListQueryFilterTypes() { return Enumerable.Empty<Type>(); }
    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
}
