using Sekiban.Core.Aggregate;
namespace WarehouseContext.Aggregates.ProductStocks;

public record ProductStock : IAggregatePayload
{
    public decimal Stocks { get; init; }
}