using AspireEventSample.ApiService.Aggregates.Branches;
using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace AspireEventSample.ApiService.Projections;
[GenerateSerializer]
public record BranchQueryFromAggregateList([property:Id(0)]string NameContains)
    : IMultiProjectionListQuery<AggregateListProjector<BranchProjector>,
        BranchQueryFromAggregateList, BranchQueryFromAggregateList.Record>
{
    [GenerateSerializer]
    public record Record(Guid BranchId, string Name);
    public static ResultBox<IEnumerable<Record>> HandleFilter(
        MultiProjectionState<AggregateListProjector<BranchProjector>> projection,
        BranchQueryFromAggregateList query,
        IQueryContext context) =>
        projection
            .Payload
            .Aggregates
            .Values
            .Where(a => a.Payload is Branch)
            .Where(a => ((Branch)a.Payload).Name.Contains(query.NameContains))
            .Select(a => new Record(a.PartitionKeys.AggregateId, ((Branch)a.Payload).Name))
            .ToResultBox();
    public static ResultBox<IEnumerable<Record>> HandleSort(
        IEnumerable<Record> filteredList,
        BranchQueryFromAggregateList query,
        IQueryContext context) => filteredList.OrderBy(m => m.Name).AsEnumerable().ToResultBox();
}