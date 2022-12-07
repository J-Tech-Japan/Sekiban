using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultiProjectionQuery : IMultiProjectionQuery<ClientLoyaltyPointMultiProjection,
    ClientLoyaltyPointMultiProjectionQuery.QueryParameter,
    ClientLoyaltyPointMultiProjectionQuery.Response>
{
    public enum QuerySortKeys
    {
        ClientName, Points
    }

    public Response HandleFilter(
        QueryParameter queryParam,
        MultiProjectionState<ClientLoyaltyPointMultiProjection> projection)
    {
        if (queryParam.BranchId is null)
        {
            return new Response(projection.Payload.Branches, projection.Payload.Records);
        }
        return new Response(
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

    public record QueryParameter(Guid? BranchId, QuerySortKeys SortKey, bool SortIsAsc = true) : IQueryParameter<Response>;

    public record Response(
        ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch> Branches,
        ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord> Records) : ClientLoyaltyPointMultiProjection(Branches, Records),
        IQueryResponse
    {
    }
}
