using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Generic multi-projector using V5 state management
/// </summary>
public record GenericTagMultiProjectorV5<TTagProjector> : IMultiProjector<GenericTagMultiProjectorV5<TTagProjector>>
    where TTagProjector : ITagProjector<TTagProjector>
{
    /// <summary>
    ///     SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionStateV5 for TagState
    /// </summary>
    public SafeUnsafeProjectionStateV5<TagState> State { get; init; } = new();

    public static string MultiProjectorName => $"GenericTagMultiProjectorV5_{TTagProjector.ProjectorName}";

    public static string MultiProjectorVersion => TTagProjector.ProjectorVersion;

    public static GenericTagMultiProjectorV5<TTagProjector> GenerateInitialPayload() => new();

    /// <summary>
    ///     Project with tag filtering - processes events based on tags
    /// </summary>
    public static ResultBox<GenericTagMultiProjectorV5<TTagProjector>> Project(
        GenericTagMultiProjectorV5<TTagProjector> payload,
        Event ev,
        List<ITag> tags)
    {
        if (tags.Count == 0)
        {
            return ResultBox.FromValue(payload);
        }

        var threshold = GetSafeWindowThreshold();

        Func<Event, IEnumerable<Guid>> getAffectedItemIds = _ => tags.Select(tag => GetTagId(tag));

        Func<Guid, TagState?, Event, TagState?> projectItem = (itemId, currentState, evt) =>
        {
            var tag = tags.FirstOrDefault(t => GetTagId(t) == itemId);
            if (tag == null)
            {
                return currentState;
            }

            var tagStateId = new TagStateId(tag, TTagProjector.ProjectorName);
            var tagState = currentState ?? TagState.GetEmpty(tagStateId);

            var newPayload = TTagProjector.Project(tagState.Payload, evt);

            if (ShouldRemoveItem(newPayload))
            {
                return null;
            }

            return tagState with
            {
                Payload = newPayload,
                Version = tagState.Version + 1,
                LastSortedUniqueId = evt.SortableUniqueIdValue,
                ProjectorVersion = TTagProjector.ProjectorVersion
            };
        };

        var newState = payload.State.UpdateSafeWindowThreshold(threshold, projectItem);
        var updatedState = newState.ProcessEvent(ev, getAffectedItemIds, projectItem);

        return ResultBox.FromValue(payload with { State = updatedState });
    }

    /// <summary>
    ///     Get unique ID for a tag
    /// </summary>
    private static Guid GetTagId(ITag tag)
    {
        if (Guid.TryParse(tag.GetTagContent(), out var guidId))
        {
            return guidId;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes($"{tag.GetTagGroup()}:{tag.GetTagContent()}");
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(bytes);
        return new Guid(hash);
    }

    /// <summary>
    ///     Check if an item should be removed based on the payload state
    /// </summary>
    private static bool ShouldRemoveItem(ITagStatePayload payload)
    {
        var deletedProperty = payload.GetType().GetProperty("IsDeleted");
        if (deletedProperty?.PropertyType == typeof(bool))
        {
            var isDeleted = deletedProperty.GetValue(payload) as bool?;
            return isDeleted == true;
        }

        return false;
    }

    /// <summary>
    ///     Get current SafeWindow threshold
    /// </summary>
    private static string GetSafeWindowThreshold()
    {
        var threshold = DateTime.UtcNow.Subtract(SafeWindow);
        return SortableUniqueId.Generate(threshold, Guid.Empty);
    }

    /// <summary>
    ///     Get all current tag states (including unsafe)
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetCurrentTagStates() => State.GetCurrentState();

    /// <summary>
    ///     Get only safe tag states
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetSafeTagStates() => State.GetSafeState();

    /// <summary>
    ///     Check if a specific tag state has unsafe modifications
    /// </summary>
    public bool IsTagStateUnsafe(Guid id) => State.IsItemUnsafe(id);

    /// <summary>
    ///     Get all state payloads from current tag states
    /// </summary>
    public IEnumerable<ITagStatePayload> GetStatePayloads()
    {
        return GetCurrentTagStates()
            .Values
            .Select(ts => ts.Payload)
            .Where(p => !ShouldRemoveItem(p));
    }

    /// <summary>
    ///     Get only safe state payloads
    /// </summary>
    public IEnumerable<ITagStatePayload> GetSafeStatePayloads()
    {
        return GetSafeTagStates()
            .Values
            .Select(ts => ts.Payload)
            .Where(p => !ShouldRemoveItem(p));
    }
}
