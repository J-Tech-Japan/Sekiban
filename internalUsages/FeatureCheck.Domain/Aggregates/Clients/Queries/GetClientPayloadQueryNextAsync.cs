using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record GetClientPayloadQueryNextAsync(string NameFilter)
    : INextAggregateListQueryAsync<Client, GetClientPayloadQuery_Response>
{
    public Task<ResultBox<IEnumerable<GetClientPayloadQuery_Response>>>
        HandleFilterAsync(IEnumerable<AggregateState<Client>> list, IQueryContext context)
    {
        return ResultBox.WrapTry(
                () => list.Where(m => m.Payload.ClientName.Contains(NameFilter))
                    .Select(m => new GetClientPayloadQuery_Response(m.Payload, m.AggregateId, m.Version)))
            .ToTask();
    }

    public Task<ResultBox<IEnumerable<GetClientPayloadQuery_Response>>> HandleSortAsync(
        IEnumerable<GetClientPayloadQuery_Response> filteredList,
        IQueryContext context)
    {
        return ResultBox.WrapTry(() => filteredList.OrderBy(m => m.Client.ClientName).AsEnumerable()).ToTask();
    }
}
