using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Generic multi-projector using V6 state management
/// </summary>
public record GenericTagMultiProjectorV6<TTagProjector> : IMultiProjector<GenericTagMultiProjectorV6<TTagProjector>>
    where TTagProjector : ITagProjector<TTagProjector>
{
    /// <summary>
    ///     SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionStateV6 for TagState
    /// </summary>
    public SafeUnsafeProjectionStateV6<TagState> State { get; init; } = new();

    public static string MultiProjectorName => $"GenericTagMultiProjectorV6_{TTagProjector.ProjectorName}";

    public static string MultiProjectorVersion => TTagProjector.ProjectorVersion;

    public static GenericTagMultiProjectorV6<TTagProjector> GenerateInitialPayload() => new();

    /// <summary>
    ///     Project with tag filtering - processes events based on tags
    /// </summary>
    public static ResultBox<GenericTagMultiProjectorV6<TTagProjector>> Project(
        GenericTagMultiProjectorV6<TTagProjector> payload,
        Event ev,
        List<ITag> tags)
    {
        // Filter tags based on what the projector expects
        if (tags.Count == 0)
        {
            // No tags, skip this event
            return ResultBox.FromValue(payload);
        }

        // Calculate SafeWindow threshold
        var threshold = GetSafeWindowThreshold();
        
        // Define ID selection function - extract IDs from tags
        Func<Event, IEnumerable<Guid>> getAffectedItemIds = (evt) =>
        {
            return tags.Select(tag => GetTagId(tag));
        };
        
        // Define projection function - project based on tag and event
        Func<Guid, TagState?, Event, TagState?> projectItem = (itemId, currentState, evt) =>
        {
            // Find the tag corresponding to this ID
            var tag = tags.FirstOrDefault(t => GetTagId(t) == itemId);
            if (tag == null)
            {
                return currentState; // No tag for this ID
            }
            
            // Create TagStateId for this tag
            var tagStateId = new TagStateId(tag, TTagProjector.ProjectorName);
            
            // If current is null, create empty TagState
            var tagState = currentState ?? TagState.GetEmpty(tagStateId);

            // Use the tag projector to project the event
            var newPayload = TTagProjector.Project(tagState.Payload, evt);

            // Check if the item should be removed (e.g., deleted items)
            if (ShouldRemoveItem(newPayload))
            {
                return null; // Remove the item
            }

            // Return updated TagState
            return tagState with
            {
                Payload = newPayload,
                Version = tagState.Version + 1,
                LastSortedUniqueId = evt.SortableUniqueIdValue,
                ProjectorVersion = TTagProjector.ProjectorVersion
            };
        };
        
        // Update threshold and process event
        var newState = payload.State.UpdateSafeWindowThreshold(threshold, getAffectedItemIds, projectItem);
        var updatedState = newState.ProcessEvent(ev, getAffectedItemIds, projectItem);

        return ResultBox.FromValue(payload with { State = updatedState });
    }

    /// <summary>
    ///     Get unique ID for a tag
    /// </summary>
    private static Guid GetTagId(ITag tag)
    {
        // Try to extract ID from tag content if it's a GUID
        if (Guid.TryParse(tag.GetTagContent(), out var guidId))
        {
            return guidId;
        }

        // Otherwise, create a deterministic GUID from the tag content
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
        // Check if the payload has an IsDeleted property set to true
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