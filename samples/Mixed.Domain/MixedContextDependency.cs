using Sekiban.Core.Dependency;
using ShippingContext;
using System.Reflection;
using WarehouseContext;
namespace Mixed.Domain;

public class MixedContextDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetEntryAssembly();

    protected override void Define()
    {
        AddDependency<ShippingDependency>()
            .AddDependency<WarehouseDependency>();
    }
}
