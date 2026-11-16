using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Implemented by the Orleans tag-consistent actor wrapper: exposes the grain's own outcome, so a failure the grain
///     reports (<c>null</c>, the latest position is unknown) stays distinguishable from a failure of the grain call itself
///     (an Orleans timeout or transport fault, which faults the task).
/// </summary>
internal interface IGrainBackedTagConsistentActor
{
    Task<string?> TryGetLatestSortableUniqueIdAsync();
}

/// <summary>
///     Reads a tag's latest sortable unique id for the tag-state grain. A failure the actor reports means the latest
///     position is unknown (<c>null</c>); a failed grain call propagates, as it did when the grain contract returned
///     its result directly.
/// </summary>
internal static class TagConsistentLatestPositionReader
{
    internal static async Task<string?> TryReadAsync(ITagConsistentActorCommon actor)
    {
        if (actor is IGrainBackedTagConsistentActor grainBacked)
        {
            return await grainBacked.TryGetLatestSortableUniqueIdAsync();
        }

        var (latest, failure) = await TaskFailureCapture.CaptureAsync(actor.GetLatestSortableUniqueIdAsync());
        return failure is null ? latest : null;
    }
}
