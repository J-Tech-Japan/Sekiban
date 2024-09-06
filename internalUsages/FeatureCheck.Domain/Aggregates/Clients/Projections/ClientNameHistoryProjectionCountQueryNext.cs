using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public record ClientNameHistoryProjectionCountQueryNext(Guid? BranchId, Guid? ClientId)
    : INextSingleProjectionQuery<ClientNameHistoryProjection, ClientNameHistoryProjectionCountQueryNext,
        ClientNameHistoryProjectionCountQuery_Response>
{
    public ResultBox<ClientNameHistoryProjectionCountQuery_Response> HandleFilter(
        IEnumerable<SingleProjectionState<ClientNameHistoryProjection>> list,
        IQueryContext context)
    {
        return new ClientNameHistoryProjectionCountQuery_Response(
            list
                .Where(m => BranchId is null || m.Payload.BranchId == BranchId)
                .Where(m => ClientId is null || ClientId == m.AggregateId)
                .Sum(m => m.Payload.ClientNames.Count));
    }
}
