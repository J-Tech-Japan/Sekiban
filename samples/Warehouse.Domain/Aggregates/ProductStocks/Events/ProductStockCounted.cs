using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public class ProductStockCounted : IEventPayload<ProductStock, ProductStockCounted>
{

    public static ProductStock OnEvent(ProductStock aggregatePayload, Event<ProductStockCounted> ev) => throw new NotImplementedException();
}
