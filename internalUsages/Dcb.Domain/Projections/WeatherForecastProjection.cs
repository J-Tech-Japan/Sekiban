using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast projection state using SafeUnsafeProjectionStateV4
/// </summary>
public record WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{

    /// <summary>
    ///     SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);
    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionStateV4
    /// </summary>
    public SafeUnsafeProjectionStateV4<WeatherForecastItem> State { get; init; } = new();

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
        // Check if event has WeatherForecastTag
        var weatherForecastTags = tags.OfType<WeatherForecastTag>().ToList();

        if (weatherForecastTags.Count == 0)
        {
            // No WeatherForecastTag, skip this event
            return ResultBox.FromValue(payload);
        }

        // Create projection function
        Func<Event, IEnumerable<ProjectionRequest<WeatherForecastItem>>> projectionFunction = (evt) =>
        {
            // Process based on event type
            return evt.Payload switch
            {
                WeatherForecastCreated created => weatherForecastTags.Select(tag =>
                    new ProjectionRequest<WeatherForecastItem>(
                        tag.ForecastId,
                        current => current != null
                            ? current with // Update existing
                            {
                                Date = created.Date.ToDateTime(TimeOnly.MinValue),
                                TemperatureC = created.TemperatureC,
                                Summary = created.Summary,
                                LastUpdated = GetEventTimestamp(evt)
                            }
                            : new WeatherForecastItem( // Create new
                                tag.ForecastId,
                                created.Date.ToDateTime(TimeOnly.MinValue),
                                created.TemperatureC,
                                created.Summary,
                                GetEventTimestamp(evt)))),
                                
                WeatherForecastUpdated updated => weatherForecastTags.Select(tag =>
                    new ProjectionRequest<WeatherForecastItem>(
                        tag.ForecastId,
                        current => current != null
                            ? current with // Update existing
                            {
                                TemperatureC = updated.TemperatureC,
                                Summary = updated.Summary,
                                LastUpdated = GetEventTimestamp(evt)
                            }
                            : null)), // Can't update non-existent item
                            
                WeatherForecastDeleted _ => weatherForecastTags.Select(tag =>
                    new ProjectionRequest<WeatherForecastItem>(
                        tag.ForecastId,
                        _ => null)), // Delete the item
                        
                _ => Enumerable.Empty<ProjectionRequest<WeatherForecastItem>>() // Unknown event type, skip
            };
        };
        
        // Calculate SafeWindow threshold
        var threshold = GetSafeWindowThreshold();
        
        // Update threshold and process event
        var newState = payload.State.UpdateSafeWindowThreshold(threshold, projectionFunction);
        var updatedState = newState.ProcessEvent(ev, projectionFunction);

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
