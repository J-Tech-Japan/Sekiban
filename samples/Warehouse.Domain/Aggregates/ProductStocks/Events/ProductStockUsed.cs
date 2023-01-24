using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public record ProductStockUsed : IEventPayload<ProductStock, ProductStockUsed>
{
    public static ProductStock OnEvent(ProductStock payload, Event<ProductStockUsed> ev) => throw new NotImplementedException();
}
