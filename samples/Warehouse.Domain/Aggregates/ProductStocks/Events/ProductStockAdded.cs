using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public record ProductStockAdded : IEventPayload<ProductStock>
{
    public decimal AddedAmount { get; init; }
    public ProductStock OnEvent(ProductStock payload, IEvent ev) => new()
    {
        Stocks = payload.Stocks + AddedAmount
    };
}
