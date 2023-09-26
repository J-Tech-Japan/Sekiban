using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Commands;

public record UseProductStock : ICommand<ProductStock>
{
    public Guid GetAggregateId() => throw new NotImplementedException();

    public class Handler : ICommandHandler<ProductStock, UseProductStock>
    {

        public IEnumerable<IEventPayloadApplicableTo<ProductStock>> HandleCommand(
            Func<AggregateState<ProductStock>> getAggregateState,
            UseProductStock command) =>
            throw new NotImplementedException();
    }
}
