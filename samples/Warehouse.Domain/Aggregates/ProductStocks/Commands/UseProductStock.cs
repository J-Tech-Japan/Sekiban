using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace WarehouseContext.Aggregates.ProductStocks.Commands;

public record UseProductStock : ICommand<ProductStock>
{
    public Guid GetAggregateId() => throw new NotImplementedException();

    public class Handler : ICommandHandler<ProductStock, UseProductStock>
    {

        public IAsyncEnumerable<IEventPayload<ProductStock>> HandleCommandAsync(
            Func<AggregateState<ProductStock>> getAggregateState,
            UseProductStock command) => throw new NotImplementedException();
    }
}
