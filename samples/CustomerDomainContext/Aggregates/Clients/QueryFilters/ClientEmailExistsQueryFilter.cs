using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace CustomerDomainContext.Aggregates.Clients.QueryFilters;

public class ClientEmailExistsQueryFilter : IAggregateQueryFilterDefinition<Client, ClientContents, ClientEmailExistsQueryFilter.QueryParameter, bool>
{

    public bool HandleFilter(QueryParameter queryParam, IEnumerable<AggregateDto<ClientContents>> list)
    {
        return list.Any(c => c.Contents.ClientEmail == queryParam.Email);
    }
    public bool HandleSort(QueryParameter queryParam, bool projections)
    {
        return projections;
    }
    public record QueryParameter(string Email) : IQueryParameter;
}
