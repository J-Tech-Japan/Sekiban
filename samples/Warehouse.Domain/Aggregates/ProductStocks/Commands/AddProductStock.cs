using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Shipping.Port.ProductExistsPorts;
using WarehouseContext.Aggregates.ProductStocks.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Commands;

public record AddProductStock : ICommand<ProductStock>
{
    public Guid ProductId { get; init; }
    public decimal AddedAmount { get; init; }
    public Guid GetAggregateId() => ProductId;
    public class Handler : ICommandHandler<ProductStock, AddProductStock>
    {
        private readonly IProductExistsPort _productExistsPort;
        public Handler(IProductExistsPort productExistsPort) => _productExistsPort = productExistsPort;

        public async IAsyncEnumerable<IEventPayload<ProductStock>> HandleCommandAsync(
            Func<AggregateState<ProductStock>> getAggregateState,
            AddProductStock command)
        {
            await Task.CompletedTask;
            if (!await _productExistsPort.ProductExistsAsync(command.ProductId))
            {
                throw new Exception("Product does not exist");
            }
            yield return new ProductStockAdded
                { AddedAmount = command.AddedAmount };
        }
    }
}
