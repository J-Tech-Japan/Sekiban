using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public class ClientNameHistoryProjectionCountQuery : ISingleProjectionQuery<ClientNameHistoryProjection,
    ClientNameHistoryProjectionCountQuery.Parameter, ClientNameHistoryProjectionCountQuery_Response>
{
    public ClientNameHistoryProjectionCountQuery_Response HandleFilter(
        Parameter queryParam,
        IEnumerable<SingleProjectionState<ClientNameHistoryProjection>> list)
    {
        return new ClientNameHistoryProjectionCountQuery_Response(
            list
                .Where(m => queryParam.BranchId is null || m.Payload.BranchId == queryParam.BranchId)
                .Where(m => queryParam.ClientId is null || queryParam.ClientId == m.AggregateId)
                .Sum(m => m.Payload.ClientNames.Count));
    }

    public record Parameter(Guid? BranchId, Guid? ClientId)
        : IQueryParameter<ClientNameHistoryProjectionCountQuery_Response>;
}
