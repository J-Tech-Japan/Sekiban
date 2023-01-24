using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public record ProductStockAdded : IEventPayload<ProductStock, ProductStockAdded>
{
    public decimal AddedAmount { get; init; }
    public static ProductStock OnEvent(ProductStock payload, Event<ProductStockAdded> ev) => new()
    {
        Stocks = payload.Stocks + ev.Payload.AddedAmount
    };
}
