using Sekiban.Core.Query.MultProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using System.Collections.Immutable;
namespace Customer.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultipleMultiProjectionQuery : IMultiProjectionQuery<ClientLoyaltyPointMultiProjection,
    ClientLoyaltyPointMultiProjection.PayloadDefinition, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
    ClientLoyaltyPointMultiProjection.PayloadDefinition>
{
    public enum QuerySortKeys
    {
        ClientName, Points
    }
    public ClientLoyaltyPointMultiProjection.PayloadDefinition HandleFilter(
        QueryParameter queryParam,
        MultiProjectionState<ClientLoyaltyPointMultiProjection.PayloadDefinition> projection)
    {
        if (queryParam.BranchId is null) { return projection.Payload; }
        return new ClientLoyaltyPointMultiProjection.PayloadDefinition
        {
            Branches = projection.Payload.Branches.Where(x => x.BranchId == queryParam.BranchId).ToImmutableList(),
            Records = projection.Payload.Records.Where(m => m.BranchId == queryParam.BranchId).ToImmutableList()
        };
    }
    public ClientLoyaltyPointMultiProjection.PayloadDefinition HandleSortAndPagingIfNeeded(
        QueryParameter queryParam,
        ClientLoyaltyPointMultiProjection.PayloadDefinition response)
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
