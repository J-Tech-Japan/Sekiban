using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using ShippingContext.Aggregates.Products.Events;
namespace ShippingContext.Aggregates.Products.Commands;

public record CreateProduct : ICommand<Product>
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Price { get; init; }

    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<Product, CreateProduct>
    {
        public async IAsyncEnumerable<IEventPayload<Product>> HandleCommandAsync(
            Func<AggregateState<Product>> getAggregateState,
            CreateProduct command)
        {
            await Task.CompletedTask;
            yield return new ProductCreated
                { Name = command.Name, Code = command.Code, Price = command.Price };
        }
    }
}
