using Microsoft.Extensions.DependencyInjection;
using Mixed.Domain;
using Sekiban.Testing.SingleProjections;
using Shipping.Port.ProductExistsPorts;
using ShippingContext.Aggregates.Products.Commands;
using ShippingContext.Interactors;
using WarehouseContext.Aggregates.ProductStocks;
using WarehouseContext.Aggregates.ProductStocks.Commands;
namespace Mixed.Test;

public class ProductStockSpec : AggregateTest<ProductStock, MixedContextDependency>
{
    protected override void SetupDependency(IServiceCollection serviceCollection)
    {
        serviceCollection.AddTransient<IProductExistsPort, ProductExistsInteractor>();
    }
    [Fact]
    public void AddTest()
    {
        var productId = RunEnvironmentCommand(
            new CreateProduct
                { Name = "Product 1", Code = "001", Price = 100 });
        WhenCommand(
                new AddProductStock
                    { ProductId = productId, AddedAmount = 100 })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new ProductStock
                    { Stocks = 100 });
    }
    [Fact]
    public void AddTestProductShouldExist()
    {
        RunEnvironmentCommand(
            new CreateProduct
                { Name = "Product 1", Code = "001", Price = 100 });
        WhenCommand(
                new AddProductStock
                    { ProductId = Guid.NewGuid(), AddedAmount = 100 })
            .ThenThrowsAnException();
    }
}
