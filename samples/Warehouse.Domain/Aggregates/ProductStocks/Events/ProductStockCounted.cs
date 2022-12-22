using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Events;

public class ProductStockCounted : IEventPayload<ProductStock>
{

    public ProductStock OnEvent(ProductStock payload, IEvent ev) => throw new NotImplementedException();
}
