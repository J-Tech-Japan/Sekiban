using Sekiban.Core.Command;
using Sekiban.Core.Events;
using ShippingContext.Aggregates.Products.Events;
namespace ShippingContext.Aggregates.Products.Commands;

public record CreateProduct : ICommand<Product>
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Price { get; init; }

    public class Handler : ICommandHandler<Product, CreateProduct>
    {
        public IEnumerable<IEventPayloadApplicableTo<Product>> HandleCommand(
            CreateProduct command,
            ICommandContext<Product> context)
        {
            yield return new ProductCreated { Name = command.Name, Code = command.Code, Price = command.Price };
        }
        public Guid SpecifyAggregateId(CreateProduct command) => Guid.NewGuid();
    }
}
