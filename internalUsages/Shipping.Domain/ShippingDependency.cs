using Sekiban.Core.Dependency;
using ShippingContext.Aggregates.Products;
using ShippingContext.Aggregates.Products.Commands;
using ShippingContext.Aggregates.Products.Queries;
using System.Reflection;
namespace ShippingContext;

public class ShippingDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    public override void Define()
    {
        AddAggregate<Product>().AddCommandHandler<CreateProduct, CreateProduct.Handler>().AddAggregateQuery<ProductExistsQuery>();
    }
}
