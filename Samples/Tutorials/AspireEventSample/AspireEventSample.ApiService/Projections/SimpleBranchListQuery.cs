using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace AspireEventSample.ApiService.Projections;

[GenerateSerializer]
public record SimpleBranchListQuery([property: Id(0)] string NameContain)
    : IMultiProjectionListQuery<BranchMultiProjector, SimpleBranchListQuery, BranchMultiProjector.BranchRecord>
{
    public static ResultBox<IEnumerable<BranchMultiProjector.BranchRecord>> HandleFilter(
        MultiProjectionState<BranchMultiProjector> projection,
        SimpleBranchListQuery query,
        IQueryContext context)
    {
        return ResultBox.Ok(projection.Payload.Branches.Values.Where(b => b.BranchName.Contains(query.NameContain)));
    }

    public static ResultBox<IEnumerable<BranchMultiProjector.BranchRecord>> HandleSort(
        IEnumerable<BranchMultiProjector.BranchRecord> filteredList,
        SimpleBranchListQuery query,
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.BranchId).AsEnumerable().ToResultBox();
    }
}