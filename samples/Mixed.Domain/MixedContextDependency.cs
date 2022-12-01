using System.Reflection;
using Sekiban.Core.Dependency;
using ShippingContext;
using WarehouseContext;

namespace Mixed.Domain;

public class MixedContextDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly()
    {
        throw new NotImplementedException();
    }

    protected override void Define()
    {
        AddDependency<ShippingDependency>()
            .AddDependency<WarehouseDependency>();
    }
}
