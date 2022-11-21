using Sekiban.Core.Dependency;
using System.Reflection;
namespace WarehouseContext;

public class WarehouseDependency : DomainDependencyDefinitionBase
{

    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    protected override void Define()
    {
    }
}
