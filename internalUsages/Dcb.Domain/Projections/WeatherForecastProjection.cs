using Dcb.Domain.Weather;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast projection state using SafeUnsafeProjectionState
/// </summary>
[GenerateSerializer]
public record WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{

    /// <summary>
    ///     SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);
    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionState
    /// </summary>
    [Id(0)]
    public SafeUnsafeProjectionState<Guid, WeatherForecastItem> State { get; init; } = new();

    public static string MultiProjectorName => "WeatherForecastProjection";

    public static WeatherForecastProjection GenerateInitialPayload() => new();

    public static string MultiProjectorVersion => "1.0.0";

    /// <summary>
    ///     Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public static ResultBox<WeatherForecastProjection> Project(
        WeatherForecastProjection payload,
        Event ev,
        List<ITag> tags)
    {
        Console.WriteLine(
            $"[WeatherForecastProjection.Project] Processing event: {ev.EventType}, Tags: {string.Join(", ", tags.Select(t => t.GetType().Name))}");

        // Check if event has WeatherForecastTag
        var weatherForecastTags = tags.OfType<WeatherForecastTag>().ToList();

        if (weatherForecastTags.Count == 0)
        {
            Console.WriteLine("[WeatherForecastProjection.Project] No WeatherForecastTag found, skipping event");
            // No WeatherForecastTag, skip this event
            return ResultBox.FromValue(payload);
        }

        Console.WriteLine(
            $"[WeatherForecastProjection.Project] Found {weatherForecastTags.Count} WeatherForecastTag(s)");

        // Function to get affected item IDs
        Func<Event, IEnumerable<Guid>> getAffectedItemIds = evt =>
        {
            // Return the forecast IDs from the tags
            return weatherForecastTags.Select(tag => tag.ForecastId);
        };

        // Function to project a single item
        Func<Guid, WeatherForecastItem?, Event, WeatherForecastItem?> projectItem = (forecastId, current, evt) =>
        {
            // Process based on event type
            return evt.Payload switch
            {
                WeatherForecastCreated created => current != null
                    ? current with // Update existing
                    {
                        Location = created.Location,
                        Date = created.Date.ToDateTime(TimeOnly.MinValue),
                        TemperatureC = created.TemperatureC,
                        Summary = created.Summary,
                        LastUpdated = GetEventTimestamp(evt)
                    }
                    : new WeatherForecastItem( // Create new
                        forecastId,
                        created.Location,
                        created.Date.ToDateTime(TimeOnly.MinValue),
                        created.TemperatureC,
                        created.Summary,
                        GetEventTimestamp(evt)),

                WeatherForecastUpdated updated => current != null
                    ? current with // Update existing
                    {
                        Location = updated.Location,
                        TemperatureC = updated.TemperatureC,
                        Summary = updated.Summary,
                        LastUpdated = GetEventTimestamp(evt)
                    }
                    : null, // Can't update non-existent item

                LocationNameChanged locationChanged => current != null
                    ? current with // Update location name only
                    {
                        Location = locationChanged.NewLocationName,
                        LastUpdated = GetEventTimestamp(evt)
                    }
                    : null, // Can't update non-existent item

                WeatherForecastDeleted _ => null, // Delete the item

                _ => current // Unknown event type, keep current state
            };
        };

        // Calculate SafeWindow threshold
        var threshold = GetSafeWindowThreshold();

        // Update threshold and process event
        var newState = payload.State with { SafeWindowThreshold = threshold };
        var updatedState = newState.ProcessEvent(ev, getAffectedItemIds, projectItem);

        Console.WriteLine(
            $"[WeatherForecastProjection.Project] After processing - Current forecasts: {updatedState.GetCurrentState().Count}, Safe forecasts: {updatedState.GetSafeState().Count}");

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
    ///     Extract timestamp from event's SortableUniqueId
    /// </summary>
    private static DateTime GetEventTimestamp(Event ev)
    {
        // SortableUniqueId contains timestamp in first 19 digits
        var sortableId = new SortableUniqueId(ev.SortableUniqueIdValue);
        return sortableId.GetDateTime();
    }

    /// <summary>
    ///     Get all current weather forecasts (including unsafe)
    /// </summary>
    public IReadOnlyDictionary<Guid, WeatherForecastItem> GetCurrentForecasts() => State.GetCurrentState();

    /// <summary>
    ///     Get only safe weather forecasts
    /// </summary>
    public IReadOnlyDictionary<Guid, WeatherForecastItem> GetSafeForecasts() => State.GetSafeState();

    /// <summary>
    ///     Check if a specific forecast has unsafe modifications
    /// </summary>
    public bool IsForecastUnsafe(Guid forecastId) => State.IsItemUnsafe(forecastId);
}
