using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast projection using TagState with SafeUnsafeProjectionState
/// </summary>
public record WeatherForecastProjectorWithTagStateProjector :
    IMultiProjector<WeatherForecastProjectorWithTagStateProjector>,
    ISafeAndUnsafeStateAccessor<WeatherForecastProjectorWithTagStateProjector>
{

    // We no longer need an instance since we'll use static methods

    /// <summary>
    ///     SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);
    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionState for TagState
    /// </summary>
    public SafeUnsafeProjectionState<Guid, TagState> State { get; init; } = new();

    public static string MultiProjectorName => "WeatherForecastProjectorWithTagStateProjector";

    public static WeatherForecastProjectorWithTagStateProjector GenerateInitialPayload() => new();

    public static string MultiProjectorVersion => "1.0.0";

    /// <summary>
    ///     Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public static ResultBox<WeatherForecastProjectorWithTagStateProjector> Project(
        WeatherForecastProjectorWithTagStateProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        TimeProvider timeProvider)
    {
        // Check if event has WeatherForecastTag
        var weatherForecastTags = tags.OfType<WeatherForecastTag>().ToList();

        if (weatherForecastTags.Count == 0)
        {
            // No WeatherForecastTag, skip this event
            return ResultBox.FromValue(payload);
        }

        // Function to get affected item IDs
        Func<Event, IEnumerable<Guid>> getAffectedItemIds = evt => weatherForecastTags.Select(tag => tag.ForecastId);

        // Function to project a single item
        Func<Guid, TagState?, Event, TagState?> projectItem = (forecastId, current, evt) =>
            ProjectTagState(forecastId, current, evt, weatherForecastTags);

        // Calculate SafeWindow threshold
        var threshold = GetSafeWindowThreshold(timeProvider);

        // Process event with threshold
        var updatedState = payload.State.ProcessEvent(ev, getAffectedItemIds, projectItem, threshold);

        return ResultBox.FromValue(payload with { State = updatedState });
    }
    /// <summary>
    ///     Project a single tag state
    /// </summary>
    private static TagState? ProjectTagState(
        Guid forecastId,
        TagState? current,
        Event ev,
        List<WeatherForecastTag> tags)
    {
        // Find the tag that corresponds to this forecast ID
        var tag = tags.FirstOrDefault(t => t.ForecastId == forecastId);
        if (tag == null)
        {
            return current; // Tag not found, keep current state
        }

        // Create TagStateId for this tag
        var tagStateId = new TagStateId(tag, WeatherForecastProjector.ProjectorName);

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
    ///     Get current SafeWindow threshold
    /// </summary>
    private static string GetSafeWindowThreshold(TimeProvider timeProvider)
    {
        var threshold = timeProvider.GetUtcNow().UtcDateTime.Subtract(SafeWindow);
        return SortableUniqueId.Generate(threshold, Guid.Empty);
    }

    /// <summary>
    ///     Get all current tag states (including unsafe)
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetCurrentTagStates() => State.GetCurrentState();

    /// <summary>
    ///     Get only safe tag states
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetSafeTagStates()
    {
        // Need to provide threshold and projection functions for safe state
        // For now, return empty until we can determine proper safe window
        return new Dictionary<Guid, TagState>();
    }

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
        return GetSafeTagStates()
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
    public WeatherForecastProjectorWithTagStateProjector GetSafeState(
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes,
        TimeProvider timeProvider) =>
        // The State already manages safe/unsafe internally
        // We return the same instance since SafeUnsafeProjectionState handles it
        this;

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Get unsafe state
    /// </summary>
    public WeatherForecastProjectorWithTagStateProjector GetUnsafeState(DcbDomainTypes domainTypes, TimeProvider timeProvider) =>
        // Return current state (includes unsafe)
        this;

    /// <summary>
    ///     ISafeAndUnsafeStateAccessor - Process event
    /// </summary>
    public ISafeAndUnsafeStateAccessor<WeatherForecastProjectorWithTagStateProjector> ProcessEvent(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes,
        TimeProvider timeProvider)
    {
        // Extract tags from event - specifically looking for WeatherForecastTag
        var tags = new List<ITag>();
        foreach (var tagString in evt.Tags)
        {
            // Parse the tag string format "group:content"
            var parts = tagString.Split(':', 2);
            if (parts.Length == 2 && parts[0] == WeatherForecastTag.TagGroupName)
            {
                // This is a WeatherForecastTag - parse the GUID content
                if (Guid.TryParse(parts[1], out var forecastId))
                {
                    tags.Add(new WeatherForecastTag(forecastId));
                }
            }
            // Ignore other tags since this projector only processes WeatherForecastTag
        }

        // Use the static Project method with provided domainTypes
        var result = Project(this, evt, tags, domainTypes, timeProvider);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to project event: {result.GetException()}");
        }

        var projected = result.GetValue();

        // Update tracking information
        return projected with
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
