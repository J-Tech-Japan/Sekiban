using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using System.Collections.Immutable;
namespace Customer.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultiProjectionQuery : IMultiProjectionQuery<ClientLoyaltyPointMultiProjection,
    ClientLoyaltyPointMultiProjectionQuery.QueryParameter,
    ClientLoyaltyPointMultiProjection>
{
    public enum QuerySortKeys
    {
        ClientName, Points
    }
    public ClientLoyaltyPointMultiProjection HandleFilter(
        QueryParameter queryParam,
        MultiProjectionState<ClientLoyaltyPointMultiProjection> projection)
    {
        if (queryParam.BranchId is null) { return projection.Payload; }
        return new ClientLoyaltyPointMultiProjection(
            projection.Payload.Branches.Where(x => x.BranchId == queryParam.BranchId).ToImmutableList(),
            projection.Payload.Records.Where(m => m.BranchId == queryParam.BranchId).ToImmutableList());
    }
    public ClientLoyaltyPointMultiProjection HandleSortAndPagingIfNeeded(
        QueryParameter queryParam,
        ClientLoyaltyPointMultiProjection response)
    {
        if (queryParam.SortKey == QuerySortKeys.ClientName)
        {
            return response with
            {
                Records = queryParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.ClientName).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.ClientName).ToImmutableList()
            };
        }
        if (queryParam.SortKey == QuerySortKeys.Points)
        {
            return response with
            {
                Records = queryParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.Point).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.Point).ToImmutableList()
            };
        }
        return response with { Records = response.Records.OrderBy(x => x.ClientName).ToImmutableList() };
    }
    public record QueryParameter(Guid? BranchId, QuerySortKeys SortKey, bool SortIsAsc = true) : IQueryParameter;
}
