using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace ShippingContext.Aggregates.Products.Commands;

public record Product : ICommandBase<Products.Product>
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Price { get; init; }

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : CreateCommandHandlerBase<Products.Product, Product>
    {
        protected override IAsyncEnumerable<IApplicableEvent<Products.Product>> ExecCreateCommandAsync(
            Func<AggregateState<Products.Product>> getAggregateState,
            Product command) => throw new NotImplementedException();
    }
}
