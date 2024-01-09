using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Shipping.Port.ProductExistsPorts;
using WarehouseContext.Aggregates.ProductStocks.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Commands;

public record AddProductStock : ICommand<ProductStock>
{
    public Guid ProductId { get; init; }
    public decimal AddedAmount { get; init; }
    public Guid GetAggregateId() => ProductId;
    public class Handler : ICommandHandlerAsync<ProductStock, AddProductStock>
    {
        private readonly IProductExistsPort _productExistsPort;
        public Handler(IProductExistsPort productExistsPort) => _productExistsPort = productExistsPort;

        public async IAsyncEnumerable<IEventPayloadApplicableTo<ProductStock>> HandleCommandAsync(
            AddProductStock command,
            ICommandContext<ProductStock> context)
        {
            yield return !await _productExistsPort.ProductExistsAsync(command.ProductId)
                ? throw new SekibanTypeNotFoundException("Product does not exist")
                : (IEventPayloadApplicableTo<ProductStock>)new ProductStockAdded { AddedAmount = command.AddedAmount };
        }
    }
}
