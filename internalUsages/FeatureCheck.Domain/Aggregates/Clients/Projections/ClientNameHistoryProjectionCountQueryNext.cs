using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public record ClientNameHistoryProjectionCountQueryNext(Guid? BranchId, Guid? ClientId)
    : INextSingleProjectionQuery<ClientNameHistoryProjection, ClientNameHistoryProjectionCountQueryNext,
        ClientNameHistoryProjectionCountQuery_Response>
{
    public static ResultBox<ClientNameHistoryProjectionCountQuery_Response> HandleFilter(
        IEnumerable<SingleProjectionState<ClientNameHistoryProjection>> list,
        ClientNameHistoryProjectionCountQueryNext query,
        IQueryContext context)
    {
        return new ClientNameHistoryProjectionCountQuery_Response(
            list
                .Where(m => query.BranchId is null || m.Payload.BranchId == query.BranchId)
                .Where(m => query.ClientId is null || query.ClientId == m.AggregateId)
                .Sum(m => m.Payload.ClientNames.Count));
    }
}
