using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast projection using TagState with SafeUnsafeProjectionStateV6
/// </summary>
public record WeatherForecastProjectorWithTagStateProjectorV6 : IMultiProjector<WeatherForecastProjectorWithTagStateProjectorV6>
{
    /// <summary>
    ///     SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);
    
    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionStateV6 for TagState
    /// </summary>
    public SafeUnsafeProjectionStateV6<TagState> State { get; init; } = new();

    public static string MultiProjectorName => "WeatherForecastProjectorWithTagStateProjectorV6";

    public static WeatherForecastProjectorWithTagStateProjectorV6 GenerateInitialPayload() => new();

    public static string MultiProjectorVersion => "1.0.0";

    /// <summary>
    ///     Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public static ResultBox<WeatherForecastProjectorWithTagStateProjectorV6> Project(
        WeatherForecastProjectorWithTagStateProjectorV6 payload,
        Event ev,
        List<ITag> tags)
    {
        // Check if event has WeatherForecastTag
        var weatherForecastTags = tags.OfType<WeatherForecastTag>().ToList();

        if (weatherForecastTags.Count == 0)
        {
            // No WeatherForecastTag, skip this event
            return ResultBox.FromValue(payload);
        }

        // Define ID selection function - extract forecast IDs from tags
        Func<Event, IEnumerable<Guid>> getAffectedItemIds = (evt) =>
        {
            return weatherForecastTags.Select(tag => tag.ForecastId);
        };

        // Define projection function - project TagState based on event
        Func<Guid, TagState?, Event, TagState?> projectItem = (forecastId, currentState, evt) =>
        {
            // Find the tag for this forecast ID
            var tag = weatherForecastTags.FirstOrDefault(t => t.ForecastId == forecastId);
            if (tag == null)
            {
                return currentState; // No tag for this ID
            }

            // Create TagStateId for this tag
            var tagStateId = new TagStateId(tag, WeatherForecastProjector.ProjectorName);

            // If current is null, create empty TagState
            var tagState = currentState ?? TagState.GetEmpty(tagStateId);

            // Use WeatherForecastProjector to project the event
            var newPayload = WeatherForecastProjector.Project(tagState.Payload, evt);

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
                LastSortedUniqueId = evt.SortableUniqueIdValue,
                ProjectorVersion = WeatherForecastProjector.ProjectorVersion
            };
        };
        
        // Calculate SafeWindow threshold
        var threshold = GetSafeWindowThreshold();
        
        // Update threshold and process event
        var newState = payload.State.UpdateSafeWindowThreshold(threshold, getAffectedItemIds, projectItem);
        var updatedState = newState.ProcessEvent(ev, getAffectedItemIds, projectItem);

        return ResultBox.FromValue(payload with { State = updatedState });
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
}