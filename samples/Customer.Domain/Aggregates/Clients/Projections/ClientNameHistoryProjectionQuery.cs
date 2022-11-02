using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Customer.Domain.Aggregates.Clients.Projections;

public enum ClientNameHistoryProjectionQuerySortKeys
{
    BranchId,
    ClientId,
    ClientName,
    ClientEmail
}
// ReSharper disable once ClassNeverInstantiated.Global
public class ClientNameHistoryProjectionQuery : ISingleProjectionListQuery<Client, ClientNameHistoryProjection,
    ClientNameHistoryProjection.PayloadDefinition, ClientNameHistoryProjectionQuery.ClientNameHistoryProjectionParameter,
    ClientNameHistoryProjectionQuery.ClientNameHistoryProjectionQueryResponse>
{
    public IEnumerable<ClientNameHistoryProjectionQueryResponse> HandleFilter(
        ClientNameHistoryProjectionParameter queryParam,
        IEnumerable<ProjectionState<ClientNameHistoryProjection.PayloadDefinition>> list)
    {
        return (from projection in list
                from name in projection.Payload.ClientNames
                select new ClientNameHistoryProjectionQueryResponse(
                    projection.Payload.BranchId,
                    projection.AggregateId,
                    name.Name,
                    projection.Payload.ClientEmail,
                    name.DateChanged)).Where(
            m => (queryParam.BranchId == null || m.BranchId == queryParam.BranchId) &&
                (queryParam.ClientId == null || m.ClientId == queryParam.ClientId));
    }
    public IEnumerable<ClientNameHistoryProjectionQueryResponse> HandleSort(
        ClientNameHistoryProjectionParameter queryParam,
        IEnumerable<ClientNameHistoryProjectionQueryResponse> projections)
    {
        if (queryParam.SortKey == null)
        {
            return projections.OrderBy(m => m.BranchId).ThenBy(m => m.ClientEmail);
        }

        switch (queryParam.SortKey)
        {
            case ClientNameHistoryProjectionQuerySortKeys.BranchId:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.BranchId) : projections.OrderByDescending(m => m.BranchId);
            case ClientNameHistoryProjectionQuerySortKeys.ClientId:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.ClientId) : projections.OrderByDescending(m => m.ClientId);
            case ClientNameHistoryProjectionQuerySortKeys.ClientName:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.ClientName) : projections.OrderByDescending(m => m.ClientName);
            case ClientNameHistoryProjectionQuerySortKeys.ClientEmail:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.ClientEmail) : projections.OrderByDescending(m => m.ClientEmail);
        }
        return projections;
    }
    public record ClientNameHistoryProjectionParameter(
        int? PageSize,
        int? PageNumber,
        Guid? BranchId,
        Guid? ClientId,
        ClientNameHistoryProjectionQuerySortKeys? SortKey,
        bool SortIsAsc = true) : IQueryPagingParameter;
    public record ClientNameHistoryProjectionQueryResponse(Guid BranchId, Guid ClientId, string ClientName, string ClientEmail, DateTime NameSetAt);
}
