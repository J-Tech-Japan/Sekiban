using System;
using System.Collections.Generic;
using System.Linq;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Common;

namespace Dcb.Domain.Projections;

/// <summary>
/// Weather forecast item in projection
/// </summary>
public record WeatherForecastItem(
    Guid ForecastId,
    DateTime Date,
    int TemperatureC,
    string? Summary,
    DateTime LastUpdated
);

/// <summary>
/// Weather forecast projection state using SafeUnsafeProjectionStateV3
/// </summary>
public record WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>, IMultiProjectionPayload
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
    
    public string GetVersion() => "1.0.0";
    
    /// <summary>
    /// Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public ResultBox<WeatherForecastProjection> Project(WeatherForecastProjection payload, Event ev, List<ITag> tags)
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
                (tag, current) => CreateOrUpdateForecast(current, tag.ForecastId, created, ev)
            ),
            WeatherForecastUpdated updated => ProcessEventWithTags<WeatherForecastUpdated>(
                newState, ev, weatherForecastTags,
                (tag, current) => UpdateForecast(current, updated, ev)
            ),
            WeatherForecastDeleted _ => ProcessEventWithTags<WeatherForecastDeleted>(
                newState, ev, weatherForecastTags,
                (tag, current) => null // Delete the item
            ),
            _ => newState // Unknown event type, skip
        };
        
        return ResultBox.FromValue(payload with { State = updatedState });
    }
    
    /// <summary>
    /// Generic method to process events with tags
    /// </summary>
    private SafeUnsafeProjectionStateV3<WeatherForecastItem> ProcessEventWithTags<TPayload>(
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
    /// Create new or update existing forecast
    /// </summary>
    private WeatherForecastItem? CreateOrUpdateForecast(
        WeatherForecastItem? current,
        Guid forecastId,
        WeatherForecastCreated created,
        Event ev)
    {
        if (current != null)
        {
            // Already exists, update it
            return current with
            {
                Date = created.Date.ToDateTime(TimeOnly.MinValue),
                TemperatureC = created.TemperatureC,
                Summary = created.Summary,
                LastUpdated = GetEventTimestamp(ev)
            };
        }
        
        // Create new
        return new WeatherForecastItem(
            forecastId,
            created.Date.ToDateTime(TimeOnly.MinValue),
            created.TemperatureC,
            created.Summary,
            GetEventTimestamp(ev)
        );
    }
    
    /// <summary>
    /// Update existing forecast
    /// </summary>
    private WeatherForecastItem? UpdateForecast(
        WeatherForecastItem? current,
        WeatherForecastUpdated updated,
        Event ev)
    {
        if (current == null)
        {
            // Item doesn't exist, can't update
            return null;
        }
        
        // Update existing item
        return current with
        {
            TemperatureC = updated.TemperatureC,
            Summary = updated.Summary,
            LastUpdated = GetEventTimestamp(ev)
        };
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