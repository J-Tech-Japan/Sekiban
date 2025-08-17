using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast projection state using SafeUnsafeProjectionStateV5
/// </summary>
public record WeatherForecastProjectionV5 : IMultiProjector<WeatherForecastProjectionV5>
{
    /// <summary>
    ///     SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);
    
    /// <summary>
    ///     Internal state managed by SafeUnsafeProjectionStateV5
    /// </summary>
    public SafeUnsafeProjectionStateV5<WeatherForecastItem> State { get; init; } = new();

    public static string MultiProjectorName => "WeatherForecastProjectionV5";

    public static WeatherForecastProjectionV5 GenerateInitialPayload() => new();

    public static string MultiProjectorVersion => "1.0.0";

    /// <summary>
    ///     Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public static ResultBox<WeatherForecastProjectionV5> Project(
        WeatherForecastProjectionV5 payload,
        Event ev,
        List<ITag> tags)
    {
        var weatherForecastTags = tags.OfType<WeatherForecastTag>().ToList();

        if (weatherForecastTags.Count == 0)
        {
            return ResultBox.FromValue(payload);
        }

        Func<Event, IEnumerable<Guid>> getAffectedItemIds = _ =>
        {
            return weatherForecastTags.Select(tag => tag.ForecastId);
        };

        Func<Guid, WeatherForecastItem?, Event, WeatherForecastItem?> projectItem = (forecastId, current, evt) =>
        {
            var tag = weatherForecastTags.FirstOrDefault(t => t.ForecastId == forecastId);
            if (tag == null)
            {
                return current;
            }

            return evt.Payload switch
            {
                WeatherForecastCreated created => current != null
                    ? current with
                    {
                        Date = created.Date.ToDateTime(TimeOnly.MinValue),
                        TemperatureC = created.TemperatureC,
                        Summary = created.Summary,
                        LastUpdated = GetEventTimestamp(evt)
                    }
                    : new WeatherForecastItem(
                        forecastId,
                        created.Date.ToDateTime(TimeOnly.MinValue),
                        created.TemperatureC,
                        created.Summary,
                        GetEventTimestamp(evt)),

                WeatherForecastUpdated updated => current != null
                    ? current with
                    {
                        TemperatureC = updated.TemperatureC,
                        Summary = updated.Summary,
                        LastUpdated = GetEventTimestamp(evt)
                    }
                    : null,

                WeatherForecastDeleted _ => null,

                _ => current
            };
        };
        
        var threshold = GetSafeWindowThreshold();
        
        var newState = payload.State.UpdateSafeWindowThreshold(threshold, projectItem);
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
    ///     Extract timestamp from event's SortableUniqueId
    /// </summary>
    private static DateTime GetEventTimestamp(Event ev)
    {
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
