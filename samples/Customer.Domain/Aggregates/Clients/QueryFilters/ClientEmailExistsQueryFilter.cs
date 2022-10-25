using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Customer.Domain.Aggregates.Clients.QueryFilters;

public class ClientEmailExistsQueryFilter : IAggregateQueryFilterDefinition<Client, ClientEmailExistsQueryFilter.QueryParameter, bool>
{

    public bool HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<Client>> list)
    {
        return list.Any(c => c.Payload.ClientEmail == queryParam.Email);
    }
    public bool HandleSort(QueryParameter queryParam, bool projections)
    {
        return projections;
    }
    public record QueryParameter(string Email) : IQueryParameter;
}
