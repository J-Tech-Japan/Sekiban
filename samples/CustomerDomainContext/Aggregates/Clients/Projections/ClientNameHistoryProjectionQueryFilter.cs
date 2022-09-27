using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace CustomerDomainContext.Aggregates.Clients.Projections;

public enum ClientNameHistoryProjectionQueryFilterSortKeys
{
    BranchId,
    ClientId,
    ClientName,
    ClientEmail
}
// ReSharper disable once ClassNeverInstantiated.Global
public record ClientNameHistoryProjectionParameter(
    int? PageSize,
    int? PageNumber,
    Guid? BranchId,
    Guid? ClientId,
    ClientNameHistoryProjectionQueryFilterSortKeys? SortKey,
    bool SortIsAsc = true) : IQueryFilterParameter;
public record ClientNameHistoryProjectionQueryResponse(Guid BranchId, Guid ClientId, string ClientName, string ClientEmail, DateTime NameSetAt);
public class ClientNameHistoryProjectionQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<Client, ClientNameHistoryProjection,
    ClientNameHistoryProjection.ContentsDefinition, ClientNameHistoryProjectionParameter, ClientNameHistoryProjectionQueryResponse>
{

    public IEnumerable<ClientNameHistoryProjectionQueryResponse> HandleFilter(
        ClientNameHistoryProjectionParameter queryParam,
        IEnumerable<SingleAggregateProjectionDto<ClientNameHistoryProjection.ContentsDefinition>> list)
    {
        return (from projection in list
                from name in projection.Contents.ClientNames
                select new ClientNameHistoryProjectionQueryResponse(
                    projection.Contents.BranchId,
                    projection.AggregateId,
                    name.Name,
                    projection.Contents.ClientEmail,
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
            case ClientNameHistoryProjectionQueryFilterSortKeys.BranchId:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.BranchId) : projections.OrderByDescending(m => m.BranchId);
            case ClientNameHistoryProjectionQueryFilterSortKeys.ClientId:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.ClientId) : projections.OrderByDescending(m => m.ClientId);
            case ClientNameHistoryProjectionQueryFilterSortKeys.ClientName:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.ClientName) : projections.OrderByDescending(m => m.ClientName);
            case ClientNameHistoryProjectionQueryFilterSortKeys.ClientEmail:
                return queryParam.SortIsAsc ? projections.OrderBy(m => m.ClientEmail) : projections.OrderByDescending(m => m.ClientEmail);
        }
        return projections;
    }
}
