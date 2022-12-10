using Sekiban.Core.Dependency;
using System.Reflection;
using WarehouseContext.Aggregates.ProductStocks;
using WarehouseContext.Aggregates.ProductStocks.Commands;
namespace WarehouseContext;

public class WarehouseDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    protected override void Define()
    {
        AddAggregate<ProductStock>()
            .AddCommandHandler<AddProductStock, AddProductStock.Handler>()
            .AddCommandHandler<ReportProductStockCount, ReportProductStockCount.Handler>()
            .AddCommandHandler<UseProductStock, UseProductStock.Handler>();
    }
}
