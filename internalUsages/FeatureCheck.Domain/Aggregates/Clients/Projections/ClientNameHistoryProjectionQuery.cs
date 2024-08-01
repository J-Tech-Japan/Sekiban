using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

// ReSharper disable once ClassNeverInstantiated.Global
public class ClientNameHistoryProjectionQuery : ISingleProjectionListQuery<ClientNameHistoryProjection,
    ClientNameHistoryProjectionQuery.Parameter,
    ClientNameHistoryProjectionQuery.Response>
{
    public IEnumerable<Response> HandleFilter(Parameter queryParam,
        IEnumerable<SingleProjectionState<ClientNameHistoryProjection>> list)
    {
        return (from projection in list
                from name in projection.Payload.ClientNames
                select new Response(projection.Payload.BranchId, projection.AggregateId, name.Name,
                    projection.Payload.ClientEmail, name.DateChanged))
            .Where(
                m => (queryParam.BranchId == null || m.BranchId == queryParam.BranchId) &&
                    (queryParam.ClientId == null || m.ClientId == queryParam.ClientId));
    }

    public IEnumerable<Response> HandleSort(Parameter queryParam, IEnumerable<Response> filteredList)
    {
        return (queryParam.SortKey, queryParam.SortIsAsc) switch
        {
            (null, _) => filteredList.OrderBy(m => m.BranchId).ThenBy(m => m.ClientEmail),
            (ClientNameHistoryProjectionQuerySortKeys.BranchId, true) => filteredList.OrderBy(m => m.BranchId),
            (ClientNameHistoryProjectionQuerySortKeys.BranchId, false) => filteredList.OrderByDescending(
                m => m.BranchId),
            (ClientNameHistoryProjectionQuerySortKeys.ClientId, true) => filteredList.OrderBy(m => m.ClientId),
            (ClientNameHistoryProjectionQuerySortKeys.ClientId, false) => filteredList.OrderByDescending(
                m => m.ClientId),
            (ClientNameHistoryProjectionQuerySortKeys.ClientName, true) => filteredList.OrderBy(m => m.ClientName),
            (ClientNameHistoryProjectionQuerySortKeys.ClientName, false) => filteredList.OrderByDescending(m =>
                m.ClientName),
            (ClientNameHistoryProjectionQuerySortKeys.ClientEmail, true) => filteredList.OrderBy(m => m.ClientEmail),
            (ClientNameHistoryProjectionQuerySortKeys.ClientEmail, false) => filteredList.OrderByDescending(m =>
                m.ClientEmail),
            _ => filteredList
        };
    }

    public record Parameter(
        int? PageSize,
        int? PageNumber,
        Guid? BranchId,
        Guid? ClientId,
        ClientNameHistoryProjectionQuerySortKeys? SortKey,
        bool SortIsAsc = true) : IListQueryPagingParameter<Response>;

    public record Response(Guid BranchId, Guid ClientId, string ClientName, string ClientEmail, DateTime NameSetAt)
        : IQueryResponse;
}
