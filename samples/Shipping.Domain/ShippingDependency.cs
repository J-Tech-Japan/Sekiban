using Sekiban.Core.Dependency;
using System.Reflection;
namespace ShippingContext;

public class ShippingDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    protected override void Define()
    {
    }
}
