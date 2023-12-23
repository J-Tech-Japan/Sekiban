using Sekiban.Core.Aggregate;
namespace WarehouseContext.Aggregates.ProductStocks;

public record ProductStock : IAggregatePayload<ProductStock>
{
    public decimal Stocks { get; init; }
    public static ProductStock CreateInitialPayload(ProductStock? _) => new();
}
