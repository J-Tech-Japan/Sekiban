using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace MultiTenant.Domain.Aggregates.Clients.Queries;

public class ClientListQuery : ITenantAggregateListQuery<ClientPayload, ClientListQuery.Parameter, ClientListQuery.Response>
{
    public IEnumerable<Response> HandleFilter(Parameter queryParam, IEnumerable<AggregateState<ClientPayload>> list) =>
        list.Select(m => new Response(m.Payload.Name));
    public IEnumerable<Response> HandleSort(Parameter queryParam, IEnumerable<Response> filteredList) => filteredList.OrderBy(m => m.Name);
    public record Parameter(string TenantId) : ITenantListQueryParameter<Response>;

    public record Response(string Name) : IQueryResponse;
}
