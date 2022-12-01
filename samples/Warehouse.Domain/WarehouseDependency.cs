using System.Reflection;
using Sekiban.Core.Dependency;

namespace WarehouseContext;

public class WarehouseDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly()
    {
        return Assembly.GetExecutingAssembly();
    }

    protected override void Define()
    {
    }
}
