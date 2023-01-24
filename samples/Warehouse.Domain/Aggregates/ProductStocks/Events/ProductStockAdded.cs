using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public record ProductStockAdded : IEventPayload<ProductStock, ProductStockAdded>
{
    public decimal AddedAmount { get; init; }
    public static ProductStock OnEvent(ProductStock aggregatePayload, Event<ProductStockAdded> ev) => new()
    {
        Stocks = aggregatePayload.Stocks + ev.Payload.AddedAmount
    };
}
