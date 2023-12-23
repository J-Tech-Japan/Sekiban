using Sekiban.Core.Query.QueryModel;
using Shipping.Port.ProductExistsPorts;
using ShippingContext.Aggregates.Products.Queries;
namespace ShippingContext.Interactors;

public class ProductExistsInteractor : IProductExistsPort
{
    private readonly IQueryExecutor _executor;
    public ProductExistsInteractor(IQueryExecutor executor) => _executor = executor;
    public async Task<bool> ProductExistsAsync(Guid productId)
    {
        await Task.CompletedTask;
        var response = await _executor.ExecuteAsync(new ProductExistsQuery.Parameter(productId));
        return response.Exists;
    }
}
