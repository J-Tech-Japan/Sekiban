using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public class GetClientPayloadQuery : IAggregateListQuery<Client, GetClientPayloadQuery.Parameter, GetClientPayloadQuery.Response>
{
    public IEnumerable<Response> HandleFilter(Parameter queryParam, IEnumerable<AggregateState<Client>> list) =>
        list.Where(m => m.Payload.ClientName.Contains(queryParam.NameFilter)).Select(m => new Response(m.Payload, m.AggregateId, m.Version));
    public IEnumerable<Response> HandleSort(Parameter queryParam, IEnumerable<Response> filteredList) =>
        filteredList.OrderBy(m => m.Client.ClientName);
    public record Response(Client Client, Guid ClientId, int Version) : IQueryResponse;
    public record Parameter(string NameFilter) : IListQueryParameter<Response>, IQueryParameterMultiProjectionOptionSettable
    {
        // default value is null, but can add a options.
        public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; init; } = null;
    }
}
