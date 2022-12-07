using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public class ClientNameHistoryProjectionCountQuery : ISingleProjectionQuery<ClientNameHistoryProjection,
    ClientNameHistoryProjectionCountQuery.Parameter, int>
{
    public int HandleFilter(Parameter queryParam, IEnumerable<SingleProjectionState<ClientNameHistoryProjection>> list)
    {
        return list
            .Where(m => queryParam.BranchId is null || m.Payload.BranchId == queryParam.BranchId)
            .Where(m => queryParam.ClientId is null || queryParam.ClientId == m.AggregateId)
            .Sum(m => m.Payload.ClientNames.Count);
    }

    public record Parameter(Guid? BranchId, Guid? ClientId) : IQueryParameterCommon;
}
