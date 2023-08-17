using Sekiban.Core.Dependency;
using ShippingContext;
using System.Reflection;
using WarehouseContext;
namespace Mixed.Domain;

public class MixedContextDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    public override void Define()
    {
        AddDependency<ShippingDependency>()
            .AddDependency<WarehouseDependency>();
    }
}
