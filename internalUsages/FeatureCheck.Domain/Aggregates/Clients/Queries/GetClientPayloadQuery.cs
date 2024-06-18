using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public class GetClientPayloadQuery : IAggregateListQuery<Client, GetClientPayloadQuery.Parameter, GetClientPayloadQuery_Response>
{
    public IEnumerable<GetClientPayloadQuery_Response> HandleFilter(Parameter queryParam, IEnumerable<AggregateState<Client>> list) =>
        list.Where(m => m.Payload.ClientName.Contains(queryParam.NameFilter))
            .Select(m => new GetClientPayloadQuery_Response(m.Payload, m.AggregateId, m.Version));
    public IEnumerable<GetClientPayloadQuery_Response> HandleSort(Parameter queryParam, IEnumerable<GetClientPayloadQuery_Response> filteredList) =>
        filteredList.OrderBy(m => m.Client.ClientName);
    public record Parameter(string NameFilter) : IListQueryParameter<GetClientPayloadQuery_Response>, IQueryParameterMultiProjectionOptionSettable
    {
        // default value is null, but can add a options.
        public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; init; } = null;
    }
}
// public class TestingQueryOld(IQueryExecutor queryExecutor)
// {
//     public async Task TestHandle()
//     {
//         var result2 = await queryExecutor.ExecuteAsync(new GetClientPayloadQuery.Parameter("test@me.com"));
//     }
// }