using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Projections;

/// <summary>
/// Weather forecast projection state using SafeUnsafeProjectionStateV3
/// </summary>
public record WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{
    /// <summary>
    /// Internal state managed by SafeUnsafeProjectionStateV3
    /// </summary>
    public SafeUnsafeProjectionStateV3<WeatherForecastItem> State { get; init; } = new();
    
    /// <summary>
    /// SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);
    
    public static string GetMultiProjectorName() => "WeatherForecastProjection";
    
    public static WeatherForecastProjection GenerateInitialPayload() => new();
    
    public static string GetVersion() => "1.0.0";
    
    /// <summary>
    /// Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public static ResultBox<WeatherForecastProjection> Project(WeatherForecastProjection payload, Event ev, List<ITag> tags)
    {
        // Check if event has WeatherForecastTag
        var weatherForecastTags = tags
            .OfType<WeatherForecastTag>()
            .ToList();
        
        if (weatherForecastTags.Count == 0)
        {
            // No WeatherForecastTag, skip this event
            return ResultBox.FromValue(payload);
        }
        
        // Calculate SafeWindow threshold
        var threshold = GetSafeWindowThreshold();
        var newState = payload.State.UpdateSafeWindowThreshold(threshold);
        
        // Process the event based on its type
        var updatedState = ev.Payload switch
        {
            WeatherForecastCreated created => ProcessEventWithTags<WeatherForecastCreated>(
                newState, ev, weatherForecastTags,
                (tag, current) => current != null
                    ? current with  // Update existing
                    {
                        Date = created.Date.ToDateTime(TimeOnly.MinValue),
                        TemperatureC = created.TemperatureC,
                        Summary = created.Summary,
                        LastUpdated = GetEventTimestamp(ev)
                    }
                    : new WeatherForecastItem(  // Create new
                        tag.ForecastId,
                        created.Date.ToDateTime(TimeOnly.MinValue),
                        created.TemperatureC,
                        created.Summary,
                        GetEventTimestamp(ev)
                    )
            ),
            WeatherForecastUpdated updated => ProcessEventWithTags<WeatherForecastUpdated>(
                newState, ev, weatherForecastTags,
                (tag, current) => current != null
                    ? current with  // Update existing
                    {
                        TemperatureC = updated.TemperatureC,
                        Summary = updated.Summary,
                        LastUpdated = GetEventTimestamp(ev)
                    }
                    : null  // Can't update non-existent item
            ),
            WeatherForecastDeleted _ => ProcessEventWithTags<WeatherForecastDeleted>(
                newState, ev, weatherForecastTags,
                (tag, current) => null  // Delete the item
            ),
            _ => newState  // Unknown event type, skip
        };
        
        return ResultBox.FromValue(payload with { State = updatedState });
    }
    
    /// <summary>
    /// Generic method to process events with tags
    /// </summary>
    private static SafeUnsafeProjectionStateV3<WeatherForecastItem> ProcessEventWithTags<TPayload>(
        SafeUnsafeProjectionStateV3<WeatherForecastItem> state,
        Event ev,
        List<WeatherForecastTag> tags,
        Func<WeatherForecastTag, WeatherForecastItem?, WeatherForecastItem?> projector)
        where TPayload : class
    {
        return state.ProcessEvent<TPayload>(ev, _ =>
            tags.Select(tag => new ProjectionRequest<WeatherForecastItem>(
                tag.ForecastId,
                current => projector(tag, current)
            ))
        );
    }
    
    /// <summary>
    /// Get current SafeWindow threshold
    /// </summary>
    private static string GetSafeWindowThreshold()
    {
        var threshold = DateTime.UtcNow.Subtract(SafeWindow);
        return SortableUniqueId.Generate(threshold, Guid.Empty);
    }
    
    /// <summary>
    /// Extract timestamp from event's SortableUniqueId
    /// </summary>
    private static DateTime GetEventTimestamp(Event ev)
    {
        // SortableUniqueId contains timestamp in first 19 digits
        var sortableId = new SortableUniqueId(ev.SortableUniqueIdValue);
        return sortableId.GetDateTime();
    }
    
    /// <summary>
    /// Get all current weather forecasts (including unsafe)
    /// </summary>
    public IReadOnlyDictionary<Guid, WeatherForecastItem> GetCurrentForecasts()
        => State.GetCurrentState();
    
    /// <summary>
    /// Get only safe weather forecasts
    /// </summary>
    public IReadOnlyDictionary<Guid, WeatherForecastItem> GetSafeForecasts()
        => State.GetSafeState();
    
    /// <summary>
    /// Check if a specific forecast has unsafe modifications
    /// </summary>
    public bool IsForecastUnsafe(Guid forecastId)
        => State.IsItemUnsafe(forecastId);
}