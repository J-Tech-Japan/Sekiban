using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public enum ClientNameHistoryProjectionQuerySortKeys
{
    BranchId,
    ClientId,
    ClientName,
    ClientEmail
}

// ReSharper disable once ClassNeverInstantiated.Global
public class ClientNameHistoryProjectionQuery : ISingleProjectionListQuery<ClientNameHistoryProjection,
    ClientNameHistoryProjectionQuery.Parameter,
    ClientNameHistoryProjectionQuery.Response>
{
    public IEnumerable<Response> HandleFilter(
        Parameter queryParam,
        IEnumerable<SingleProjectionState<ClientNameHistoryProjection>> list)
    {
        return (from projection in list
                from name in projection.Payload.ClientNames
                select new Response(
                    projection.Payload.BranchId,
                    projection.AggregateId,
                    name.Name,
                    projection.Payload.ClientEmail,
                    name.DateChanged)).Where(
            m => (queryParam.BranchId == null || m.BranchId == queryParam.BranchId) &&
                (queryParam.ClientId == null || m.ClientId == queryParam.ClientId));
    }

    public IEnumerable<Response> HandleSort(
        Parameter queryParam,
        IEnumerable<Response> filteredList)
    {
        if (queryParam.SortKey == null)
        {
            return filteredList.OrderBy(m => m.BranchId).ThenBy(m => m.ClientEmail);
        }

        switch (queryParam.SortKey)
        {
            case ClientNameHistoryProjectionQuerySortKeys.BranchId:
                return queryParam.SortIsAsc
                    ? filteredList.OrderBy(m => m.BranchId)
                    : filteredList.OrderByDescending(m => m.BranchId);
            case ClientNameHistoryProjectionQuerySortKeys.ClientId:
                return queryParam.SortIsAsc
                    ? filteredList.OrderBy(m => m.ClientId)
                    : filteredList.OrderByDescending(m => m.ClientId);
            case ClientNameHistoryProjectionQuerySortKeys.ClientName:
                return queryParam.SortIsAsc
                    ? filteredList.OrderBy(m => m.ClientName)
                    : filteredList.OrderByDescending(m => m.ClientName);
            case ClientNameHistoryProjectionQuerySortKeys.ClientEmail:
                return queryParam.SortIsAsc
                    ? filteredList.OrderBy(m => m.ClientEmail)
                    : filteredList.OrderByDescending(m => m.ClientEmail);
        }

        return filteredList;
    }

    public record Parameter(
        int? PageSize,
        int? PageNumber,
        Guid? BranchId,
        Guid? ClientId,
        ClientNameHistoryProjectionQuerySortKeys? SortKey,
        bool SortIsAsc = true) : IListQueryPagingParameter<Response>;

    public record Response(Guid BranchId, Guid ClientId, string ClientName, string ClientEmail, DateTime NameSetAt) : IQueryResponse;
}
