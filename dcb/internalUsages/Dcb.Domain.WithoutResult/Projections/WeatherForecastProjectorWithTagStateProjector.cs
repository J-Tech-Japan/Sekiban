using Dcb.Domain.WithoutResult.Weather;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Projections;

/// <summary>
///     Weather forecast projection using TagState with SafeUnsafeProjectionState
///     Implements custom serialization to properly handle SafeUnsafeProjectionState
/// </summary>
public record WeatherForecastProjectorWithTagStateProjector :
    IMultiProjectorWithCustomSerialization<WeatherForecastProjectorWithTagStateProjector>,
    ISafeAndUnsafeStateAccessor<WeatherForecastProjectorWithTagStateProjector>
{

    // We no longer need an instance since we'll use static methods

    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionState for TagState
    /// </summary>
    public SafeUnsafeProjectionState<Guid, TagState> State { get; init; } = new();
    public int SafeVersion
    {
        get
        {
            var current = State.GetCurrentState();
            return current.Values.Sum(ts => ts.Version);
        }
    }

    public static string MultiProjectorName => "WeatherForecastProjectorWithTagStateProjector";

    public static WeatherForecastProjectorWithTagStateProjector GenerateInitialPayload() => new();

    public static string MultiProjectorVersion => "1.0.0";

    public static SerializationResult Serialize(DcbDomainTypes domainTypes, string safeWindowThreshold, WeatherForecastProjectorWithTagStateProjector payload)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
        {
            throw new ArgumentException("safeWindowThreshold must be supplied", nameof(safeWindowThreshold));
        }
        // Build safe state using supplied threshold
        Func<Event, IEnumerable<Guid>> getAffectedItemIds = evt => evt.Payload switch
        {
            WeatherForecastCreated created => new[] { created.ForecastId },
            WeatherForecastUpdated updated => new[] { updated.ForecastId },
            WeatherForecastDeleted deleted => new[] { deleted.ForecastId },
            LocationNameChanged changed => new[] { changed.ForecastId },
            _ => Enumerable.Empty<Guid>()
        };
        Func<Guid, TagState?, Event, TagState?> projectItem = (forecastId, current, ev) => ProjectTagState(forecastId, current, ev);
        var safeDict = payload.State.GetSafeState(safeWindowThreshold, getAffectedItemIds, projectItem);
        var items = new List<object>(safeDict.Count);
        foreach (var (id, ts) in safeDict)
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
        var dto = new { v = 1, items };
        var json = System.Text.Json.JsonSerializer.Serialize(dto, domainTypes.JsonSerializerOptions);
        var rawBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var originalSize = rawBytes.LongLength;
        var compressed = GzipCompression.Compress(rawBytes);
        var compressedSize = compressed.LongLength;
        return new SerializationResult(compressed, originalSize, compressedSize);
    }

    public static WeatherForecastProjectorWithTagStateProjector Deserialize(DcbDomainTypes domainTypes, string safeWindowThreshold, ReadOnlySpan<byte> data)
    {
        var json = GzipCompression.DecompressToString(data);
        var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json, domainTypes.JsonSerializerOptions);
        var map = new Dictionary<Guid, TagState>();
        var tagProjectorName = WeatherForecastProjector.ProjectorName;
        if (obj != null && obj.TryGetPropertyValue("items", out var itemsNode) && itemsNode is System.Text.Json.Nodes.JsonArray arr)
        {
            foreach (var n in arr)
            {
                if (n is System.Text.Json.Nodes.JsonObject item)
                {
                    var id = item["id"]?.GetValue<Guid>() ?? Guid.Empty;
                    var type = item["type"]?.GetValue<string>() ?? string.Empty;
                    var payloadJson = item["payload"]?.GetValue<string>() ?? "{}";
                    var version = item["version"]?.GetValue<int>() ?? 0;
                    var last = item["last"]?.GetValue<string>() ?? string.Empty;

                    ITagStatePayload payload;
                    var bytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
                    var rb = domainTypes.TagStatePayloadTypes.DeserializePayload(type, bytes);
                    if (!rb.IsSuccess) continue;
                    payload = rb.GetValue();

                    var tag = new WeatherForecastTag(id);
                    var tagStateId = new TagStateId(tag, tagProjectorName);
                    var ts = TagState.GetEmpty(tagStateId) with
                    {
                        Payload = payload,
                        Version = version,
                        LastSortedUniqueId = last,
                        ProjectorVersion = WeatherForecastProjector.ProjectorVersion
                    };
                    map[id] = ts;
                }
            }
        }
        var state = SafeUnsafeProjectionState<Guid, TagState>.FromCurrentData(map);
        return new WeatherForecastProjectorWithTagStateProjector { State = state };
    }

    /// <summary>
    ///     Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public static WeatherForecastProjectorWithTagStateProjector Project(
        WeatherForecastProjectorWithTagStateProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
    SortableUniqueId safeWindowThreshold)
    {
        // Check if event has WeatherForecastTag
        var weatherForecastTags = tags.OfType<WeatherForecastTag>().ToList();

        if (weatherForecastTags.Count == 0)
        {
            // No WeatherForecastTag, skip this event
            return payload;
        }

        // Use the weather forecast tags from the parameter, not from the event
        // This is important for test scenarios where tags are passed separately
        var affectedForecastIds = weatherForecastTags.Select(t => t.ForecastId).ToList();

        // Function to get affected item IDs for any event passed by state
        // For consistency with actual runtime, we still check evt.Tags but fallback to passed tags
        Func<Event, IEnumerable<Guid>> getAffectedItemIds = evt =>
        {
            // First try to get tags from the event itself
            var eventTags = evt.Tags
                .Select(domainTypes.TagTypes.GetTag)
                .OfType<WeatherForecastTag>()
                .Select(t => t.ForecastId)
                .ToList();

            // If no tags in event, use the affected IDs from the passed tags
            return eventTags.Count > 0 ? eventTags : affectedForecastIds;
        };

        // Function to project a single item
        Func<Guid, TagState?, Event, TagState?> projectItem = (forecastId, current, evt) =>
            ProjectTagState(forecastId, current, evt);

        // Process event with threshold (pass as string value)
        var updatedState = payload.State.ProcessEvent(ev, getAffectedItemIds, projectItem, safeWindowThreshold.Value);

        return payload with { State = updatedState };
    }
    /// <summary>
    ///     Project a single tag state
    /// </summary>
    private static TagState? ProjectTagState(
        Guid forecastId,
        TagState? current,
        Event ev)
    {
        // Create TagStateId for this tag
        var tagStateId = new TagStateId(new WeatherForecastTag(forecastId), WeatherForecastProjector.ProjectorName);

        // If current is null, create empty TagState
        var tagState = current ?? TagState.GetEmpty(tagStateId);

        // Use WeatherForecastProjector to project the event
        var newPayload = WeatherForecastProjector.Project(tagState.Payload, ev);

        // Check if the item was deleted
        if (newPayload is WeatherForecastState { IsDeleted: true })
        {
            return null; // Remove deleted items
        }

        // Return updated TagState
        return tagState with
        {
            Payload = newPayload,
            Version = tagState.Version + 1,
            LastSortedUniqueId = ev.SortableUniqueIdValue,
            ProjectorVersion = WeatherForecastProjector.ProjectorVersion
        };
    }

    /// <summary>
    ///     Get all current tag states (including unsafe)
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetCurrentTagStates() => State.GetCurrentState();

    // Removed: GetSafeTagStates() — safe view construction is handled by State/Actor

    /// <summary>
    ///     Check if a specific tag state has unsafe modifications
    /// </summary>
    public bool IsTagStateUnsafe(Guid forecastId) => State.IsItemUnsafe(forecastId);

    /// <summary>
    ///     Get all weather forecast states from current tag states
    /// </summary>
    public IEnumerable<WeatherForecastState> GetWeatherForecasts()
    {
        return GetCurrentTagStates()
            .Values
            .Select(ts => ts.Payload)
            .OfType<WeatherForecastState>()
            .Where(wfs => !wfs.IsDeleted);
    }

    /// <summary>
    ///     Get only safe weather forecast states
    /// </summary>
    public IEnumerable<WeatherForecastState> GetSafeWeatherForecasts()
    {
        // Build a safe-only view using a conservative safe window consistent with tests (20s)
        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);

        // Derive affected keys directly from known domain event payloads (no DomainTypes needed)
        Func<Event, IEnumerable<Guid>> getAffectedIds = evt => evt.Payload switch
        {
            WeatherForecastCreated created => new[] { created.ForecastId },
            WeatherForecastUpdated updated => new[] { updated.ForecastId },
            WeatherForecastDeleted deleted => new[] { deleted.ForecastId },
            LocationNameChanged changed => new[] { changed.ForecastId },
            _ => Enumerable.Empty<Guid>()
        };

        // Reuse the projector's item-level projection
        Func<Guid, TagState?, Event, TagState?> projectItem = (id, current, e) => ProjectTagState(id, current, e);

        var safeDict = State.GetSafeState(safeThreshold, getAffectedIds, projectItem);

        return safeDict
            .Values
            .Select(ts => ts.Payload)
            .OfType<WeatherForecastState>()
            .Where(wfs => !wfs.IsDeleted);
    }

    #region ISafeAndUnsafeStateAccessor Implementation
    private Guid LastEventId { get; init; } = Guid.Empty;
    private string LastSortableUniqueId { get; init; } = string.Empty;
    private int Version { get; init; }

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Get safe state
    /// </summary>
    public SafeProjection<WeatherForecastProjectorWithTagStateProjector> GetSafeProjection(
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        Func<Event, IEnumerable<Guid>> getIds = evt => evt.Tags
            .Select(domainTypes.TagTypes.GetTag)
            .OfType<WeatherForecastTag>()
            .Select(t => t.ForecastId);

        Func<Guid, TagState?, Event, TagState?> projectItem = (forecastId, current, evt) => ProjectTagState(forecastId, current, evt);
        var safeDict = State.GetSafeState(safeWindowThreshold.Value, getIds, projectItem);
        var safeLast = safeDict.Count > 0
            ? safeDict.Values.Max(ts => ts.LastSortedUniqueId) ?? string.Empty
            : string.Empty;
        var version = safeDict.Values.Sum(ts => ts.Version);
        return new SafeProjection<WeatherForecastProjectorWithTagStateProjector>(this, safeLast, version);
    }

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Get unsafe state
    /// </summary>
    public UnsafeProjection<WeatherForecastProjectorWithTagStateProjector> GetUnsafeProjection(DcbDomainTypes domainTypes)
    {
        var current = State.GetCurrentState();
        var last = current.Count > 0
            ? current.Values.Max(ts => ts.LastSortedUniqueId) ?? string.Empty
            : string.Empty;
        var version = current.Values.Sum(ts => ts.Version);
        return new UnsafeProjection<WeatherForecastProjectorWithTagStateProjector>(this, last, Guid.Empty, version);
    }

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Process event
    /// </summary>
    public ISafeAndUnsafeStateAccessor<WeatherForecastProjectorWithTagStateProjector> ProcessEvent(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        // Extract tags via domainTypes and keep only WeatherForecastTag
        var tags = evt.Tags
            .Select(domainTypes.TagTypes.GetTag)
            .OfType<WeatherForecastTag>()
            .Cast<ITag>()
            .ToList();

        // Use the static Project method with provided domainTypes
        var result = Project(this, evt, tags, domainTypes, safeWindowThreshold);

        return result with
        {
            LastEventId = evt.Id,
            LastSortableUniqueId = evt.SortableUniqueIdValue,
            Version = Version + 1
        };
    }

    public Guid GetLastEventId() => LastEventId;
    public string GetLastSortableUniqueId() => LastSortableUniqueId;
    public int GetVersion() => Version;

    #endregion
}
