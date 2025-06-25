using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;

namespace Sekiban.Pure.Dapr.Parts;

/// <summary>
/// Holds partition keys and projector information for aggregate actors.
/// This is the Dapr equivalent of Orleans' PartitionKeysAndProjector.
/// </summary>
public class PartitionKeysAndProjector
{
    public PartitionKeys PartitionKeys { get; init; }
    public IAggregateProjector Projector { get; init; }

    public PartitionKeysAndProjector(PartitionKeys partitionKeys, IAggregateProjector projector)
    {
        PartitionKeys = partitionKeys ?? throw new ArgumentNullException(nameof(partitionKeys));
        Projector = projector ?? throw new ArgumentNullException(nameof(projector));
    }

    /// <summary>
    /// Creates PartitionKeysAndProjector from a grain key string
    /// </summary>
    public static ResultBox<PartitionKeysAndProjector> FromGrainKey(
        string grainKey,
        IAggregateProjectorSpecifier projectorSpecifier)
    {
        try
        {
            // Extract projector type and partition keys from the grain key
            // Format: "ProjectorType:PartitionKeysString"
            var parts = grainKey.Split(':', 2);
            if (parts.Length != 2)
            {
                return ResultBox<PartitionKeysAndProjector>.FromException(
                    new ArgumentException($"Invalid grain key format: {grainKey}"));
            }

            var projectorTypeName = parts[0];
            var partitionKeysString = parts[1];

            // Get projector from specifier
            var projectorResult = projectorSpecifier.GetProjector(projectorTypeName);
            if (!projectorResult.IsSuccess)
            {
                return ResultBox<PartitionKeysAndProjector>.FromException(
                    projectorResult.GetException());
            }

            var projector = projectorResult.UnwrapBox();

            // Parse partition keys
            var partitionKeysResult = PartitionKeys.FromPrimaryKeysString(partitionKeysString);
            if (!partitionKeysResult.IsSuccess)
            {
                return ResultBox<PartitionKeysAndProjector>.FromException(
                    partitionKeysResult.GetException());
            }

            return ResultBox<PartitionKeysAndProjector>.FromValue(
                new PartitionKeysAndProjector(partitionKeysResult.UnwrapBox(), projector));
        }
        catch (Exception ex)
        {
            return ResultBox<PartitionKeysAndProjector>.FromException(ex);
        }
    }

    /// <summary>
    /// Converts to a grain key string for the projector
    /// </summary>
    public string ToProjectorGrainKey()
    {
        return $"{Projector.GetType().Name}:{PartitionKeys.ToPrimaryKeysString()}";
    }

    /// <summary>
    /// Converts to a grain key string for the event handler
    /// </summary>
    public string ToEventHandlerGrainKey()
    {
        return PartitionKeys.ToPrimaryKeysString();
    }
}