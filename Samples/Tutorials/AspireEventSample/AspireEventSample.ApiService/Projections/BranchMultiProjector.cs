using AspireEventSample.ApiService.Aggregates.Branches;
using ResultBoxes;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Collections.Immutable;
namespace AspireEventSample.ApiService.Projections;

[GenerateSerializer]
public record BranchMultiProjector(
    [property: Id(1)] ImmutableDictionary<Guid, BranchMultiProjector.BranchRecord> Branches)
    : IMultiProjector<BranchMultiProjector>
{
    public ResultBox<BranchMultiProjector> Project(BranchMultiProjector payload, IEvent ev)
    {
        return ev.GetPayload() switch
        {
            BranchCreated branchCreated => payload with
            {
                Branches = payload.Branches.Add(
                    ev.PartitionKeys.AggregateId,
                    new BranchRecord(ev.PartitionKeys.AggregateId, branchCreated.Name))
            },
            BranchNameChanged branchNameChanged => payload.Branches.TryGetValue(
                ev.PartitionKeys.AggregateId,
                out var existingBranch)
                ? payload with
                {
                    Branches = payload.Branches.SetItem(
                        ev.PartitionKeys.AggregateId,
                        existingBranch with { BranchName = branchNameChanged.Name })
                }
                : payload,
            _ => payload
        };
    }

    public static BranchMultiProjector GenerateInitialPayload() => new(ImmutableDictionary<Guid, BranchRecord>.Empty);
    public static string GetMultiProjectorName() => nameof(BranchMultiProjector);

    [GenerateSerializer]
    public record BranchRecord([property: Id(1)] Guid BranchId, [property: Id(2)] string BranchName);
}