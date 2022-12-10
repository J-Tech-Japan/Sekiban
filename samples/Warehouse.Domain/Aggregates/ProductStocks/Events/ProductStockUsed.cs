using Sekiban.Core.Event;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public record ProductStockUsed : IEventPayload<ProductStock>
{
    public ProductStock OnEvent(ProductStock payload, IEvent ev) => throw new NotImplementedException();
}
