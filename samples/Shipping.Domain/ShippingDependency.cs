using System.Reflection;
using Sekiban.Core.Dependency;

namespace ShippingContext;

public class ShippingDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly()
    {
        return Assembly.GetExecutingAssembly();
    }

    protected override void Define()
    {
    }
}
