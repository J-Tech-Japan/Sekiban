using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using System.Collections.Immutable;
namespace Customer.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultipleMultiProjectionQueryFilter : IMultiProjectionQuery<ClientLoyaltyPointMultipleProjection,
    ClientLoyaltyPointMultipleProjection.PayloadDefinition, ClientLoyaltyPointMultipleMultiProjectionQueryFilter.QueryFilterParameter,
    ClientLoyaltyPointMultipleProjection.PayloadDefinition>
{
    public enum QuerySortKeys
    {
        ClientName, Points
    }
    public ClientLoyaltyPointMultipleProjection.PayloadDefinition HandleFilter(
        QueryFilterParameter queryFilterParam,
        MultiProjectionState<ClientLoyaltyPointMultipleProjection.PayloadDefinition> projection)
    {
        if (queryFilterParam.BranchId is null) { return projection.Payload; }
        return new ClientLoyaltyPointMultipleProjection.PayloadDefinition
        {
            Branches = projection.Payload.Branches.Where(x => x.BranchId == queryFilterParam.BranchId).ToImmutableList(),
            Records = projection.Payload.Records.Where(m => m.BranchId == queryFilterParam.BranchId).ToImmutableList()
        };
    }
    public ClientLoyaltyPointMultipleProjection.PayloadDefinition HandleSortAndPagingIfNeeded(
        QueryFilterParameter queryFilterParam,
        ClientLoyaltyPointMultipleProjection.PayloadDefinition response)
    {
        if (queryFilterParam.SortKey == QuerySortKeys.ClientName)
        {
            return response with
            {
                Records = queryFilterParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.ClientName).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.ClientName).ToImmutableList()
            };
        }
        if (queryFilterParam.SortKey == QuerySortKeys.Points)
        {
            return response with
            {
                Records = queryFilterParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.Point).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.Point).ToImmutableList()
            };
        }
        return response with { Records = response.Records.OrderBy(x => x.ClientName).ToImmutableList() };
    }
    public record QueryFilterParameter(Guid? BranchId, QuerySortKeys SortKey, bool SortIsAsc = true) : IQueryParameter;
}
