using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record GetClientPayloadQueryNextAsync(string NameFilter)
    : INextAggregateListQueryAsync<Client, GetClientPayloadQueryNextAsync, GetClientPayloadQuery_Response>
{
    public static Task<ResultBox<IEnumerable<GetClientPayloadQuery_Response>>> HandleFilterAsync(
        IEnumerable<AggregateState<Client>> list,
        GetClientPayloadQueryNextAsync query,
        IQueryContext context) =>
        ResultBox
            .WrapTry(
                () => list
                    .Where(m => m.Payload.ClientName.Contains(query.NameFilter))
                    .Select(m => new GetClientPayloadQuery_Response(m.Payload, m.AggregateId, m.Version)))
            .ToTask();

    public static Task<ResultBox<IEnumerable<GetClientPayloadQuery_Response>>> HandleSortAsync(
        IEnumerable<GetClientPayloadQuery_Response> filteredList,
        GetClientPayloadQueryNextAsync query,
        IQueryContext context) =>
        ResultBox.WrapTry(() => filteredList.OrderBy(m => m.Client.ClientName).AsEnumerable()).ToTask();
}
