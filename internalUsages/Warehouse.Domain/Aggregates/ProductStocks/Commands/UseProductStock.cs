using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Commands;

public record UseProductStock : ICommand<ProductStock>
{
    public Guid GetAggregateId() => throw new NotImplementedException();

    public class Handler : ICommandHandler<ProductStock, UseProductStock>
    {
        public IEnumerable<IEventPayloadApplicableTo<ProductStock>> HandleCommand(
            UseProductStock command,
            ICommandContext<ProductStock> context) =>
            throw new NotImplementedException();
        public Guid SpecifyAggregateId(UseProductStock command) => throw new NotImplementedException();
    }
}
