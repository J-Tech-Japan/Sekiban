using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Orleans.Parts;

public record PartitionKeysAndProjector(PartitionKeys PartitionKeys, IAggregateProjector Projector)
{
    public static ResultBox<PartitionKeysAndProjector> FromGrainKey(string grainKey, IAggregateProjectorSpecifier projectorSpecifier)
    {
        var splitted = grainKey.Split("=");
        if (splitted.Length != 2)
        {
            throw new ResultsInvalidOperationException("invalid grain key");
        }
        var partitionKeys = PartitionKeys.FromPrimaryKeysString(splitted[0]).UnwrapBox();
        return projectorSpecifier.GetProjector(splitted[1]).Remap(projector => new PartitionKeysAndProjector(partitionKeys, projector));
    }
    
    public string ToProjectorGrainKey() => $"{PartitionKeys.ToPrimaryKeysString()}={Projector.GetType().Name}";
    public string ToEventHandlerGrainKey() => PartitionKeys.ToPrimaryKeysString();
}