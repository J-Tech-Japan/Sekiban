using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace AspireEventSample.ApiService.Projections;

[GenerateSerializer]
public record BranchExistsQuery([property: Id(0)] string NameContains)
    : IMultiProjectionQuery<BranchMultiProjector, BranchExistsQuery, bool>
{
    public static ResultBox<bool> HandleQuery(
        MultiProjectionState<BranchMultiProjector> projection,
        BranchExistsQuery query,
        IQueryContext context)
    {
        return projection.Payload.Branches.Values.Any(b => b.BranchName.Contains(query.NameContains));
    }
}