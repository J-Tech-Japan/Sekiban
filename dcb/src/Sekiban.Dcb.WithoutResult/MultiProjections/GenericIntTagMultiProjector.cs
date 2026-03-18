using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Generic multi-projector that works with integer-keyed tag groups.
///     This variant uses IIntTagGroup for tags that have integer identifiers.
///     Implements custom serialization to properly handle SafeUnsafeProjectionState with int keys.
/// </summary>
/// <typeparam name="TTagProjector">The tag projector type that projects individual tag states</typeparam>
/// <typeparam name="TTagGroup">The integer-keyed tag group type (must implement IIntTagGroup)</typeparam>
/// <example>
/// <code>
/// // Example usage with YearlyStudentsTag (int-based)
/// var projector = GenericIntTagMultiProjector&lt;StudentProjector, YearlyStudentsTag&gt;.GenerateInitialPayload();
///
/// // Process event with int-keyed tag
/// var year = 2024;
/// var tag = new YearlyStudentsTag(year);
/// var result = GenericIntTagMultiProjector&lt;StudentProjector, YearlyStudentsTag&gt;.Project(
///     projector,
///     eventData,
///     new List&lt;ITag&gt; { tag },
///     domainTypes,
///     safeThreshold);
///
/// // Access state by int key
/// var states = result.GetCurrentTagStates();
/// var yearState = states[year];
/// </code>
/// </example>
public record
    GenericIntTagMultiProjector<TTagProjector, TTagGroup> :
    IMultiProjectorWithCustomSerialization<GenericIntTagMultiProjector<TTagProjector, TTagGroup>>,
    ISafeAndUnsafeStateAccessor<GenericIntTagMultiProjector<TTagProjector, TTagGroup>>
    where TTagProjector : ITagProjector<TTagProjector> where TTagGroup : IIntTagGroup<TTagGroup>
{
    /// <summary>
    ///     SafeWindow は外部 (Actor) が SortableUniqueId safeWindowThreshold として計算し ProcessEvent 経由で渡す前提。
    ///     ここでは固定値や内部計算を持たない。
    /// </summary>

    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionState for TagState with int keys
    /// </summary>
    public SafeUnsafeProjectionState<int, TagState> State { get; init; } = new();

    public static string MultiProjectorName =>
        $"GenericIntTagMultiProjector_{TTagProjector.ProjectorName}_{TTagGroup.TagGroupName}";

    public static string MultiProjectorVersion => TTagProjector.ProjectorVersion;

    public static GenericIntTagMultiProjector<TTagProjector, TTagGroup> GenerateInitialPayload() => new();

    public static SerializationResult Serialize(DcbDomainTypes domainTypes, string safeWindowThreshold, GenericIntTagMultiProjector<TTagProjector, TTagGroup> payload)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold)) throw new ArgumentException("safeWindowThreshold must be supplied", nameof(safeWindowThreshold));
        var safeProjection = payload.GetSafeProjection(new SortableUniqueId(safeWindowThreshold), domainTypes);
        var safeStates = safeProjection.State.State.GetCurrentState();
        var items = new List<object>(safeStates.Count);
        foreach (var (id, ts) in safeStates)
        {
            var payloadType = ts.Payload.GetType();
            var payloadName = payloadType.Name;
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(ts.Payload, payloadType, domainTypes.JsonSerializerOptions);
            items.Add(new
            {
                id,
                type = payloadName,
                payload = payloadJson,
                version = ts.Version,
                last = ts.LastSortedUniqueId
            });
        }
        var dto = new
        {
            v = 2,
            last = safeProjection.SafeLastSortableUniqueId,
            version = safeProjection.Version,
            items
        };
        var json = System.Text.Json.JsonSerializer.Serialize(dto, domainTypes.JsonSerializerOptions);
        var rawBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var originalSize = rawBytes.LongLength;
        var compressed = GzipCompression.Compress(rawBytes);
        var compressedSize = compressed.LongLength;
        return new SerializationResult(compressed, originalSize, compressedSize);
    }

    public static GenericIntTagMultiProjector<TTagProjector, TTagGroup> Deserialize(DcbDomainTypes domainTypes, string safeWindowThreshold, ReadOnlySpan<byte> data)
    {
        var json = GzipCompression.DecompressToString(data);
        var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json, domainTypes.JsonSerializerOptions);
        var map = new Dictionary<int, TagState>();
        var tagProjectorName = TTagProjector.ProjectorName;
        if (obj != null && obj.TryGetPropertyValue("items", out var itemsNode) && itemsNode is System.Text.Json.Nodes.JsonArray arr)
        {
            foreach (var n in arr)
            {
                if (n is System.Text.Json.Nodes.JsonObject item)
                {
                    var id = item["id"]?.GetValue<int>() ?? 0;
                    var type = item["type"]?.GetValue<string>() ?? string.Empty;
                    var payloadJson = item["payload"]?.GetValue<string>() ?? "{}";
                    var itemVersion = item["version"]?.GetValue<int>() ?? 0;
                    var itemLast = item["last"]?.GetValue<string>() ?? string.Empty;
                    var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
                    var rb = domainTypes.TagStatePayloadTypes.DeserializePayload(type, payloadBytes);
                    if (!rb.IsSuccess) continue;
                    var payload = rb.GetValue();
                    var tag = TTagGroup.FromContent(id.ToString());
                    var tagStateId = new TagStateId(tag, tagProjectorName);
                    var ts = TagState.GetEmpty(tagStateId) with
                    {
                        Payload = payload,
                        Version = itemVersion,
                        LastSortedUniqueId = itemLast,
                        ProjectorVersion = TTagProjector.ProjectorVersion
                    };
                    map[id] = ts;
                }
            }
        }
        var derivedLast = map.Count > 0
            ? map.Values.Max(ts => ts.LastSortedUniqueId) ?? string.Empty
            : string.Empty;
        var derivedVersion = map.Values.Sum(ts => ts.Version);
        var last = obj?["last"]?.GetValue<string>() ?? derivedLast;
        var version = obj?["version"]?.GetValue<int>() ?? derivedVersion;
        var state = SafeUnsafeProjectionState<int, TagState>.FromCurrentData(map);
        return new GenericIntTagMultiProjector<TTagProjector, TTagGroup>
        {
            State = state,
            LastSortableUniqueId = last,
            Version = version,
            SafeLastSortableUniqueIdValue = last,
            SafeVersionValue = version
        };
    }

    /// <summary>
    ///     Project with tag filtering - processes events based on tags (internal ResultBox version)
    /// </summary>
    private static ResultBox<GenericIntTagMultiProjector<TTagProjector, TTagGroup>> ProjectInternal(
        GenericIntTagMultiProjector<TTagProjector, TTagGroup> payload,
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

        // Get affected IDs from the passed tags for fallback
        var affectedIds = relevantTags
            .Select(tag => GetTagId(tag))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        // Function to get affected item IDs from the actual event being processed
        // First check evt.Tags for runtime, but fallback to passed tags for tests
        Func<Event, IEnumerable<int>> getAffectedItemIds = evt =>
        {
            var eventTagIds = GetAffectedTagIds(evt, domainTypes).ToList();

            // If no tags in event, use the affected IDs from the passed tags
            return eventTagIds.Count > 0 ? eventTagIds : affectedIds;
        };

        // Function to project a single item (independent of captured tag list)
        Func<int, TagState?, Event, TagState?> projectItem = (tagId, current, evt) =>
            ProjectTagState(tagId, current, evt);

        var normalizedEvent = ev.Tags.Count > 0
            ? ev
            : ev with { Tags = tags.Select(tag => tag.GetTag()).ToList() };

        // safeWindowThreshold を文字列として State へ渡し safe/unsafe 判定を内部に委譲
        var updatedState = payload.State.ProcessEvent(
            normalizedEvent,
            getAffectedItemIds,
            projectItem,
            safeWindowThreshold.Value);

        var newPayload = payload with { State = updatedState };

        return ResultBox.FromValue(newPayload);
    }

    /// <summary>
    ///     Project with tag filtering - processes events based on tags (exception-based)
    /// </summary>
    public static GenericIntTagMultiProjector<TTagProjector, TTagGroup> Project(
        GenericIntTagMultiProjector<TTagProjector, TTagGroup> payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var result = ProjectInternal(payload, ev, tags, domainTypes, safeWindowThreshold);
        return result.UnwrapBox();
    }

    /// <summary>
    ///     Project a single tag state
    /// </summary>
    private static TagState? ProjectTagState(int tagId, TagState? current, Event ev)
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

    private static IEnumerable<int> GetAffectedTagIds(Event evt, DcbDomainTypes domainTypes) =>
        evt.Tags
            .Select(domainTypes.TagTypes.GetTag)
            .OfType<TTagGroup>()
            .Select(tag => GetTagId(tag))
            .Where(id => id.HasValue)
            .Select(id => id!.Value);

    /// <summary>
    ///     Get unique ID for a tag (only for TTagGroup tags)
    /// </summary>
    private static int? GetTagId(ITag tag)
    {
        // Only process tags of our specific TTagGroup type
        if (tag is TTagGroup intTag)
        {
            return intTag.GetId();
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
    public IReadOnlyDictionary<int, TagState> GetCurrentTagStates() => State.GetCurrentState();

    /// <summary>
    ///     Get only safe tag states
    /// </summary>
    public IReadOnlyDictionary<int, TagState> GetSafeTagStates(
        string safeWindowThreshold,
        Func<Event, IEnumerable<int>> getAffectedItemKeys,
        Func<int, TagState?, Event, TagState?> projectItem) =>
        State.GetSafeState(safeWindowThreshold, getAffectedItemKeys, projectItem);

    /// <summary>
    ///     Check if a specific tag state has unsafe modifications
    /// </summary>
    public bool IsTagStateUnsafe(int id) => State.IsItemUnsafe(id);

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
        Func<Event, IEnumerable<int>> getAffectedItemKeys,
        Func<int, TagState?, Event, TagState?> projectItem)
    {
        return GetSafeTagStates(safeWindowThreshold, getAffectedItemKeys, projectItem)
            .Values.Select(ts => ts.Payload).Where(p => !ShouldRemoveItem(p));
    }

    #region ISafeAndUnsafeStateAccessor Implementation
    private Guid LastEventId { get; init; } = Guid.Empty;
    private string LastSortableUniqueId { get; init; } = string.Empty;
    private int Version { get; init; }
    private string SafeLastSortableUniqueIdValue { get; init; } = string.Empty;
    private int SafeVersionValue { get; init; }
    public int SafeVersion => SafeVersionValue;

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Get safe state
    /// </summary>
    public SafeProjection<GenericIntTagMultiProjector<TTagProjector, TTagGroup>> GetSafeProjection(
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        Func<Event, IEnumerable<int>> getIds = evt => GetAffectedTagIds(evt, domainTypes);
        Func<int, TagState?, Event, TagState?> projectItem = ProjectTagState;

        var safeDict = State.GetSafeState(safeWindowThreshold.Value, getIds, projectItem);
        var promotedUnsafeEvents = GetPromotedUnsafeEvents(safeWindowThreshold.Value);
        var safeLast = GetMaxSortableUniqueId(
            SafeLastSortableUniqueIdValue,
            promotedUnsafeEvents.Count > 0 ? promotedUnsafeEvents.Max(evt => evt.SortableUniqueIdValue) ?? string.Empty : string.Empty);
        var version = SafeVersionValue + promotedUnsafeEvents.Count;
        var safePayload = this with
        {
            State = SafeUnsafeProjectionState<int, TagState>.FromCurrentData(safeDict),
            LastEventId = Guid.Empty,
            LastSortableUniqueId = safeLast,
            Version = version,
            SafeLastSortableUniqueIdValue = safeLast,
            SafeVersionValue = version
        };
        return new SafeProjection<GenericIntTagMultiProjector<TTagProjector, TTagGroup>>(safePayload, safeLast, version);
    }

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Get unsafe state
    /// </summary>
    public UnsafeProjection<GenericIntTagMultiProjector<TTagProjector, TTagGroup>> GetUnsafeProjection(DcbDomainTypes domainTypes)
        => new(this, LastSortableUniqueId, LastEventId, Version);

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Process event
    /// </summary>
    public ISafeAndUnsafeStateAccessor<GenericIntTagMultiProjector<TTagProjector, TTagGroup>> ProcessEvent(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        // Parse tag strings into ITag instances using DomainTypes - only keep tags belonging to our tag group
        var tags = evt.Tags
            .Select(domainTypes.TagTypes.GetTag)
            .ToList();

        // Actor から渡された safeWindowThreshold をそのまま State.ProcessEvent に渡すため
        // Project 内部での SafeWindow 再計算は行わない。
        var projected = Project(this, evt, tags, domainTypes, safeWindowThreshold);
        var unsafeEventIdsAfter = projected.State.GetAllUnsafeEvents().Select(e => e.Id).ToHashSet();
        var promotedToSafe = State.GetAllUnsafeEvents()
            .Where(e => !unsafeEventIdsAfter.Contains(e.Id))
            .ToList();
        var safeVersion = SafeVersionValue + promotedToSafe.Count;
        var safeLast = GetMaxSortableUniqueId(
            SafeLastSortableUniqueIdValue,
            promotedToSafe.Count > 0 ? promotedToSafe.Max(e => e.SortableUniqueIdValue) ?? string.Empty : string.Empty);
        if (string.Compare(evt.SortableUniqueIdValue, safeWindowThreshold.Value, StringComparison.Ordinal) <= 0)
        {
            safeVersion++;
            safeLast = GetMaxSortableUniqueId(safeLast, evt.SortableUniqueIdValue);
        }

        return projected with
        {
            LastEventId = evt.Id,
            LastSortableUniqueId = evt.SortableUniqueIdValue,
            Version = Version + 1,
            SafeLastSortableUniqueIdValue = safeLast,
            SafeVersionValue = safeVersion
        };
    }

    public Guid GetLastEventId() => LastEventId;
    public string GetLastSortableUniqueId() => LastSortableUniqueId;
    public int GetVersion() => Version;

    #endregion

    private List<Event> GetPromotedUnsafeEvents(string safeWindowThreshold) =>
        State.GetAllUnsafeEvents()
            .Where(e => string.IsNullOrEmpty(safeWindowThreshold) ||
                        string.Compare(e.SortableUniqueIdValue, safeWindowThreshold, StringComparison.Ordinal) <= 0)
            .ToList();

    private static string GetMaxSortableUniqueId(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return string.Compare(left, right, StringComparison.Ordinal) >= 0 ? left : right;
    }
}
