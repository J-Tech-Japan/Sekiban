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
/// Weather forecast projection using TagState with SafeUnsafeProjectionStateV3
/// </summary>
public record WeatherForecastProjectorWithTagStateProjector : IMultiProjector<WeatherForecastProjectorWithTagStateProjector>, IMultiProjectionPayload
{
    /// <summary>
    /// Internal state managed by SafeUnsafeProjectionStateV3 for TagState
    /// </summary>
    public SafeUnsafeProjectionStateV3<TagState> State { get; init; } = new();
    
    /// <summary>
    /// Instance of the WeatherForecastProjector for projecting events
    /// </summary>
    private static readonly WeatherForecastProjector Projector = new();
    
    /// <summary>
    /// SafeWindow threshold (20 seconds by default)
    /// </summary>
    private static readonly TimeSpan SafeWindow = TimeSpan.FromSeconds(20);
    
    public static string GetMultiProjectorName() => "WeatherForecastProjectorWithTagStateProjector";
    
    public static WeatherForecastProjectorWithTagStateProjector GenerateInitialPayload() => new();
    
    public string GetVersion() => "1.0.0";
    
    /// <summary>
    /// Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public ResultBox<WeatherForecastProjectorWithTagStateProjector> Project(
        WeatherForecastProjectorWithTagStateProjector payload, 
        Event ev, 
        List<ITag> tags)
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
        
        // Process the event - the projector will handle unknown event types
        var updatedState = ProcessEventWithTags(newState, ev, weatherForecastTags);
        
        return ResultBox.FromValue(payload with { State = updatedState });
    }
    
    /// <summary>
    /// Process events with tags using WeatherForecastProjector
    /// </summary>
    private SafeUnsafeProjectionStateV3<TagState> ProcessEventWithTags(
        SafeUnsafeProjectionStateV3<TagState> state,
        Event ev,
        List<WeatherForecastTag> tags)
    {
        // Process event with the actual payload type
        // The projector will handle unknown event types by returning state unchanged
        var payloadType = ev.Payload.GetType();
        
        // Use reflection to call ProcessEvent with the correct type parameter
        var method = GetType().GetMethod(nameof(ProcessEventGeneric), 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var genericMethod = method!.MakeGenericMethod(payloadType);
        return (SafeUnsafeProjectionStateV3<TagState>)genericMethod.Invoke(this, new object[] { state, ev, tags })!;
    }
    
    /// <summary>
    /// Generic helper to process events
    /// </summary>
    private SafeUnsafeProjectionStateV3<TagState> ProcessEventGeneric<TPayload>(
        SafeUnsafeProjectionStateV3<TagState> state,
        Event ev,
        List<WeatherForecastTag> tags) where TPayload : class
    {
        return state.ProcessEvent<TPayload>(ev, _ => CreateProjectionRequests(tags, ev));
    }
    
    /// <summary>
    /// Create projection requests for each tag
    /// </summary>
    private IEnumerable<ProjectionRequest<TagState>> CreateProjectionRequests(
        List<WeatherForecastTag> tags,
        Event ev)
    {
        return tags.Select(tag =>
        {
            // Create TagStateId for this tag
            var tagStateId = new TagStateId(tag, Projector.GetType().Name);
            
            return new ProjectionRequest<TagState>(
                tag.ForecastId,
                current =>
                {
                    // If current is null, create empty TagState
                    var tagState = current ?? TagState.GetEmpty(tagStateId);
                    
                    // Use WeatherForecastProjector to project the event
                    var newPayload = Projector.Project(tagState.Payload, ev);
                    
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
                        ProjectorVersion = Projector.GetProjectorVersion()
                    };
                }
            );
        });
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
    /// Get all current tag states (including unsafe)
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetCurrentTagStates()
        => State.GetCurrentState();
    
    /// <summary>
    /// Get only safe tag states
    /// </summary>
    public IReadOnlyDictionary<Guid, TagState> GetSafeTagStates()
        => State.GetSafeState();
    
    /// <summary>
    /// Check if a specific tag state has unsafe modifications
    /// </summary>
    public bool IsTagStateUnsafe(Guid forecastId)
        => State.IsItemUnsafe(forecastId);
    
    /// <summary>
    /// Get all weather forecast states from current tag states
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
    /// Get only safe weather forecast states
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