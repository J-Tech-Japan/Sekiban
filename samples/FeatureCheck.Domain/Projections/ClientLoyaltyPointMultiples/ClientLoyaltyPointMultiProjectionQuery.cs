using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultiProjectionQuery : IMultiProjectionQuery<ClientLoyaltyPointMultiProjection,
    ClientLoyaltyPointMultiProjectionQuery.Parameter,
    ClientLoyaltyPointMultiProjectionQuery.Response>
{
    public enum QuerySortKeys
    {
        ClientName, Points
    }

    public Response HandleFilter(
        Parameter param,
        MultiProjectionState<ClientLoyaltyPointMultiProjection> projection)
    {
        if (param.BranchId is null)
        {
            return new Response(projection.Payload.Branches, projection.Payload.Records);
        }
        return new Response(
            projection.Payload.Branches.Where(x => x.BranchId == param.BranchId).ToImmutableList(),
            projection.Payload.Records.Where(m => m.BranchId == param.BranchId).ToImmutableList());
    }

    public ClientLoyaltyPointMultiProjection HandleSortAndPagingIfNeeded(
        Parameter param,
        ClientLoyaltyPointMultiProjection response)
    {
        if (param.SortKey == QuerySortKeys.ClientName)
        {
            return response with
            {
                Records = param.SortIsAsc
                    ? response.Records.OrderBy(x => x.ClientName).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.ClientName).ToImmutableList()
            };
        }
        if (param.SortKey == QuerySortKeys.Points)
        {
            return response with
            {
                Records = param.SortIsAsc
                    ? response.Records.OrderBy(x => x.Point).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.Point).ToImmutableList()
            };
        }
        return response with { Records = response.Records.OrderBy(x => x.ClientName).ToImmutableList() };
    }

    public record Parameter(Guid? BranchId, QuerySortKeys SortKey, bool SortIsAsc = true) : IQueryParameter<Response>;

    public record Response(
        ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch> Branches,
        ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord> Records) : ClientLoyaltyPointMultiProjection(Branches, Records),
        IQueryResponse
    {
    }
}
