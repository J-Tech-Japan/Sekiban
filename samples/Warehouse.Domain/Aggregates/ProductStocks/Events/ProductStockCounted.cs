using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public class ProductStockCounted : IEventPayload<ProductStock, ProductStockCounted>
{

    public static ProductStock OnEvent(ProductStock payload, Event<ProductStockCounted> ev) => throw new NotImplementedException();
}
