using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace FeatureCheck.Domain.Projections.DissolvableProjection;

public class DissolvableProjectionQuery : IMultiProjectionQuery<DissolvableEventsProjection, DissolvableProjectionQuery.Parameter,
    DissolvableProjectionQuery.Response>
{
    public Response HandleFilter(Parameter queryParam, MultiProjectionState<DissolvableEventsProjection> projection) =>
        new(projection.Payload.RecentActivities.Count);
    public record Response(int Count) : IQueryResponse;
    public record Parameter : IQueryParameter<Response>;
}
