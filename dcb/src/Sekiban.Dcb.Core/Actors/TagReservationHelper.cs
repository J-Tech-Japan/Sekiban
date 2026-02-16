using System.Diagnostics;
using ResultBoxes;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Shared reservation helpers used by both typed (ExecuteAsync) and serialized
///     (CommitSerializableEventsAsync) commit paths in CoreGeneralSekibanExecutor.
/// </summary>
public static class TagReservationHelper
{
    public static async Task<ResultBox<TagWriteReservation>> RequestReservationAsync(
        IActorObjectAccessor actorAccessor,
        ITag tag,
        string lastSortableUniqueId)
    {
        try
        {
            var tagConsistentActorId = tag.GetTag();
            var actorResult = await actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);

            if (!actorResult.IsSuccess)
            {
                return ResultBox.Error<TagWriteReservation>(actorResult.GetException());
            }

            var actor = actorResult.GetValue();
            return await actor.MakeReservationAsync(lastSortableUniqueId);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TagWriteReservation>(ex);
        }
    }

    public static async Task CancelReservationsAsync(
        IActorObjectAccessor actorAccessor,
        Dictionary<ITag, TagWriteReservation> reservations)
    {
        var cancelTasks = new List<Task>();

        foreach (var (tag, reservation) in reservations)
        {
            var task = CancelReservationAsync(actorAccessor, tag, reservation);
            cancelTasks.Add(task);
        }

        await Task.WhenAll(cancelTasks);
    }

    public static async Task ConfirmReservationsAsync(
        IActorObjectAccessor actorAccessor,
        Dictionary<ITag, TagWriteReservation> reservations)
    {
        var confirmTasks = new List<Task>();

        foreach (var (tag, reservation) in reservations)
        {
            var task = ConfirmReservationAsync(actorAccessor, tag, reservation);
            confirmTasks.Add(task);
        }

        await Task.WhenAll(confirmTasks);
    }

    public static async Task NotifyNonConsistencyTagsAsync(
        IActorObjectAccessor actorAccessor,
        HashSet<ITag> allTags,
        IEnumerable<ITag> reservedTags)
    {
        var reservedSet = reservedTags.ToHashSet();
        var nonConsistencyTags = allTags.Where(t => !reservedSet.Contains(t)).ToList();

        if (nonConsistencyTags.Count == 0) return;

        var notifyTasks = nonConsistencyTags.Select(tag => NotifyTagAsync(actorAccessor, tag));
        await Task.WhenAll(notifyTasks);
    }

    private static async Task CancelReservationAsync(
        IActorObjectAccessor actorAccessor,
        ITag tag,
        TagWriteReservation reservation)
    {
        try
        {
            var tagConsistentActorId = tag.GetTag();
            var actorResult = await actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);

            if (actorResult.IsSuccess)
            {
                var actor = actorResult.GetValue();
                await actor.CancelReservationAsync(reservation);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to cancel reservation for tag {tag.GetTag()}: {ex}");
        }
    }

    private static async Task ConfirmReservationAsync(
        IActorObjectAccessor actorAccessor,
        ITag tag,
        TagWriteReservation reservation)
    {
        try
        {
            var tagConsistentActorId = tag.GetTag();
            var actorResult = await actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);

            if (actorResult.IsSuccess)
            {
                var actor = actorResult.GetValue();
                await actor.ConfirmReservationAsync(reservation);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to confirm reservation for tag {tag.GetTag()}: {ex}");
        }
    }

    private static async Task NotifyTagAsync(
        IActorObjectAccessor actorAccessor,
        ITag tag)
    {
        try
        {
            var tagConsistentActorId = tag.GetTag();
            var actorResult = await actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);

            if (actorResult.IsSuccess)
            {
                await actorResult.GetValue().NotifyEventWrittenAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to notify tag {tag.GetTag()}: {ex}");
        }
    }
}
