using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansMultiProjectorState(
    [property: Id(1)] IMultiProjectorCommon ProjectorCommon,
    [property: Id(2)] Guid LastEventId,
    [property: Id(3)] string LastSortableUniqueId,
    [property: Id(4)] int Version,
    [property: Id(5)] int AppliedSnapshotVersion,
    [property: Id(6)] string RootPartitionKey)
{

}