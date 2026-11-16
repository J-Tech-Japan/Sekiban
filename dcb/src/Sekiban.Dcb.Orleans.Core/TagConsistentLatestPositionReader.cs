using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Implemented by the Orleans tag-consistent actor wrapper: exposes the grain's own result, so a failure the grain
///     reports stays distinguishable from a failure of the grain call itself (an Orleans timeout or transport fault).
/// </summary>
internal interface IGrainBackedTagConsistentActor
{
    Task<ResultBox<string>> GetLatestSortableUniqueIdResultAsync();
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
        var result = actor is IGrainBackedTagConsistentActor grainBacked
            ? await grainBacked.GetLatestSortableUniqueIdResultAsync()
            : await actor.GetLatestSortableUniqueIdAsync().ToResultBoxAsync();
        return result.IsSuccess ? result.GetValue() : null;
    }
}
