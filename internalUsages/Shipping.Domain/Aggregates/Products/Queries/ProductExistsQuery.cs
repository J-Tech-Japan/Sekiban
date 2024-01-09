using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace ShippingContext.Aggregates.Products.Queries;

public class ProductExistsQuery : IAggregateQuery<Product, ProductExistsQuery.Parameter, ProductExistsQuery.Response>
{
    public Response HandleFilter(Parameter queryParam, IEnumerable<AggregateState<Product>> list) =>
        new(list.Any((Func<AggregateState<Product>, bool>)(m => m.AggregateId == queryParam.ProductId)));

    public record Parameter(Guid ProductId) : IQueryParameter<Response>;

    public record Response(bool Exists) : IQueryResponse;
}
