using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace ShippingContext.Aggregates.Products.Commands;

public record CreateProduct : ICreateCommand<Product>
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Price { get; init; }

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : CreateCommandHandlerBase<Product, CreateProduct>
    {
        protected override IAsyncEnumerable<IApplicableEvent<Product>> ExecCreateCommandAsync(Func<AggregateState<Product>> getAggregateState, CreateProduct command) => throw new NotImplementedException();
    }
}
