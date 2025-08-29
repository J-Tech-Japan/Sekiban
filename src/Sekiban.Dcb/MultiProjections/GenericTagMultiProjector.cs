using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Generic multi-projector that works with any tag projector and specific tag group
/// </summary>
public record
    GenericTagMultiProjector<TTagProjector, TTagGroup> :
    IMultiProjector<GenericTagMultiProjector<TTagProjector, TTagGroup>>,
    ISafeAndUnsafeStateAccessor<GenericTagMultiProjector<TTagProjector, TTagGroup>>
    where TTagProjector : ITagProjector<TTagProjector> where TTagGroup : IGuidTagGroup<TTagGroup>
{
    /// <summary>
    ///     SafeWindow は外部 (Actor) が SortableUniqueId safeWindowThreshold として計算し ProcessEvent 経由で渡す前提。
    ///     ここでは固定値や内部計算を持たない。
    /// </summary>

    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionState for TagState
    /// </summary>
    public SafeUnsafeProjectionState<Guid, TagState> State { get; init; } = new();

    public static string MultiProjectorName =>
        $"GenericTagMultiProjector_{TTagProjector.ProjectorName}_{TTagGroup.TagGroupName}";

    public static string MultiProjectorVersion => TTagProjector.ProjectorVersion;

    public static GenericTagMultiProjector<TTagProjector, TTagGroup> GenerateInitialPayload() => new();

    /// <summary>
    ///     Project with tag filtering - processes events based on tags
    /// </summary>
    public static ResultBox<GenericTagMultiProjector<TTagProjector, TTagGroup>> Project(
        GenericTagMultiProjector<TTagProjector, TTagGroup> payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        // Filter tags to only process tags of the specified tag group type
        var relevantTags = tags.OfType<TTagGroup>().Cast<ITag>().ToList();

        if (relevantTags.Count == 0)
        {
            // No tags of the specified group, skip this event
            return ResultBox.FromValue(payload);
        }

        // Function to get affected item IDs from the actual event being processed
        // Do NOT capture current tags; resolve from evt.Tags every time for correctness
        Func<Event, IEnumerable<Guid>> getAffectedItemIds = evt => evt.Tags
            .Select(domainTypes.TagTypes.GetTag)
            .OfType<TTagGroup>()
            .Select(tag => GetTagId(tag))
            .Where(id => id.HasValue)
            .Select(id => id!.Value);

        // Function to project a single item (independent of captured tag list)
        Func<Guid, TagState?, Event, TagState?> projectItem = (tagId, current, evt) =>
            ProjectTagState(tagId, current, evt);

        // safeWindowThreshold を文字列として State へ渡し safe/unsafe 判定を内部に委譲
        var updatedState = payload.State.ProcessEvent(
            ev,
            getAffectedItemIds,
            projectItem,
            safeWindowThreshold.Value);

        var newPayload = payload with { State = updatedState };
        
        return ResultBox.FromValue(newPayload);
    }

    /// <summary>
    ///     Project a single tag state
    /// </summary>
    private static TagState? ProjectTagState(Guid tagId, TagState? current, Event ev)
    {
        // Create TagStateId for this tagId by constructing the tag from content via generic interface
        var tagGroup = TTagGroup.FromContent(tagId.ToString());
        var tagStateId = new TagStateId(tagGroup, TTagProjector.ProjectorName);

        // If current is null, create empty TagState
        var tagState = current ?? TagState.GetEmpty(tagStateId);

        // Use the tag projector to project the event
        var newPayload = TTagProjector.Project(tagState.Payload, ev);

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
            LastSortedUniqueId = ev.SortableUniqueIdValue,
            ProjectorVersion = TTagProjector.ProjectorVersion
        };
    }

    /// <summary>
    ///     Get unique ID for a tag (only for TTagGroup tags)
    /// </summary>
    private static Guid? GetTagId(ITag tag)
    {
        // Only process tags of our specific TTagGroup type
        if (tag is TTagGroup guidTag)
        {
            return guidTag.GetId();
        }

        // Ignore all other tags
        return null;
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

    // SafeWindow threshold の生成ロジックは削除。Actor が提供する値を使う設計へ移行。

    /// <summary>
    ///     Get all current tag states (including unsafe)
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetCurrentTagStates() => State.GetCurrentState();

    /// <summary>
    ///     Get only safe tag states
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetSafeTagStates(
        string safeWindowThreshold,
        Func<Event, IEnumerable<Guid>> getAffectedItemKeys,
        Func<Guid, TagState?, Event, TagState?> projectItem) => 
        State.GetSafeState(safeWindowThreshold, getAffectedItemKeys, projectItem);

    /// <summary>
    ///     Check if a specific tag state has unsafe modifications
    /// </summary>
    public bool IsTagStateUnsafe(Guid id) => State.IsItemUnsafe(id);

    /// <summary>
    ///     Get all state payloads from current tag states
    /// </summary>
    public IEnumerable<ITagStatePayload> GetStatePayloads()
    {
        var currentStates = GetCurrentTagStates();
        var payloads = currentStates.Values.Select(ts => ts.Payload).Where(p => !ShouldRemoveItem(p)).ToList();
        return payloads;
    }

    /// <summary>
    ///     Get only safe state payloads
    /// </summary>
    public IEnumerable<ITagStatePayload> GetSafeStatePayloads(
        string safeWindowThreshold,
        Func<Event, IEnumerable<Guid>> getAffectedItemKeys,
        Func<Guid, TagState?, Event, TagState?> projectItem)
    {
        return GetSafeTagStates(safeWindowThreshold, getAffectedItemKeys, projectItem)
            .Values.Select(ts => ts.Payload).Where(p => !ShouldRemoveItem(p));
    }

    #region ISafeAndUnsafeStateAccessor Implementation
    private Guid LastEventId { get; init; } = Guid.Empty;
    private string LastSortableUniqueId { get; init; } = string.Empty;
    private int Version { get; init; }

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Get safe state
    /// </summary>
    public GenericTagMultiProjector<TTagProjector, TTagGroup> GetSafeState(
        SortableUniqueId safeWindowThreshold, 
        DcbDomainTypes domainTypes, 
        TimeProvider timeProvider)
    {
        // Return this instance - the State already manages safe/unsafe separation internally
        // GetSafeStatePayloads() will return only safe items when needed for persistence
        return this;
    }

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Get unsafe state
    /// </summary>
    public GenericTagMultiProjector<TTagProjector, TTagGroup> GetUnsafeState(DcbDomainTypes domainTypes, TimeProvider timeProvider) =>
        // Return current state (includes both safe and unsafe)
        this;

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Process event
    /// </summary>
    public ISafeAndUnsafeStateAccessor<GenericTagMultiProjector<TTagProjector, TTagGroup>> ProcessEvent(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes,
        TimeProvider timeProvider)
    {
        // Parse tag strings into ITag instances using DomainTypes - only keep tags belonging to our tag group
        var tags = evt.Tags
            .Select(domainTypes.TagTypes.GetTag)
            .ToList();

    // Actor から渡された safeWindowThreshold をそのまま State.ProcessEvent に渡すため
    // Project 内部での SafeWindow 再計算は行わない。
    var result = Project(this, evt, tags, domainTypes, safeWindowThreshold);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to project event: {result.GetException()}");
        }

        var projected = result.GetValue();
        var updated = projected with { };
        return updated with
        {
            LastEventId = evt.Id,
            LastSortableUniqueId = evt.SortableUniqueIdValue,
            Version = Version + 1
        };
    }

    public Guid GetLastEventId() => LastEventId;
    public string GetLastSortableUniqueId() => LastSortableUniqueId;
    public int GetVersion() => Version;

    /// <summary>
    ///     Indicates whether there are buffered (unsafe) events affecting the current state
    ///     Used by the actor via reflection to decide if unsafe state should be returned.
    /// </summary>
    public bool HasBufferedEvents() => State.GetUnsafeKeys().Any();

    #endregion
}
