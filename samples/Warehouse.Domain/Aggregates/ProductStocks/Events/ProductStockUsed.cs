using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public record ProductStockUsed : IEventPayload<ProductStock, ProductStockUsed>
{
    public static ProductStock OnEvent(ProductStock aggregatePayload, Event<ProductStockUsed> ev) => throw new NotImplementedException();
}
