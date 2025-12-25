namespace Sekiban.Pure.Projectors;

public record MultiProjectionEventSelector(List<string> RootPartitionKeys, List<string> AggregateGroups)
    : IMultiProjectionEventSelector
{
    public static MultiProjectionEventSelector All => new(new List<string>(), new List<string>());
    public List<string> GetRootPartitionKeys() => RootPartitionKeys;
    public List<string> GetAggregateGroups() => AggregateGroups;
    public static MultiProjectionEventSelector FromProjectorGroup<TAggregateProjector>()
        where TAggregateProjector : IAggregateProjector =>
        new([], [typeof(TAggregateProjector).Name]);
}
