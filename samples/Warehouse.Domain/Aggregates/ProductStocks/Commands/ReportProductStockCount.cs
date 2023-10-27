using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace WarehouseContext.Aggregates.ProductStocks.Commands;

public record ReportProductStockCount : ICommand<ProductStock>
{

    public Guid GetAggregateId() => throw new NotImplementedException();

    public class Handler : ICommandHandler<ProductStock, ReportProductStockCount>
    {
        public IEnumerable<IEventPayloadApplicableTo<ProductStock>> HandleCommand(
            ReportProductStockCount command,
            ICommandContext<ProductStock> context) =>
            throw new NotImplementedException();
    }
}
