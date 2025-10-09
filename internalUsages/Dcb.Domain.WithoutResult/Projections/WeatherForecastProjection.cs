using Dcb.Domain.WithoutResult.Weather;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Projections;

/// <summary>
///     Simple weather forecast projection for testing DualStateProjectionWrapper
/// </summary>
[GenerateSerializer]
public record WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{
    /// <summary>
    ///     Dictionary of weather forecasts by ID
    /// </summary>
    [Id(0)]
    public Dictionary<Guid, WeatherForecastItem> Forecasts { get; init; } = new();


    public static string MultiProjectorName => "WeatherForecastProjection";

    public static WeatherForecastProjection GenerateInitialPayload() => new();

    public static string MultiProjectorVersion => "1.0.0";

    public static byte[] Serialize(DcbDomainTypes domainTypes, string safeWindowThreshold, WeatherForecastProjection safePayload)
    {
    if (string.IsNullOrWhiteSpace(safeWindowThreshold)) throw new ArgumentException("safeWindowThreshold must be supplied", nameof(safeWindowThreshold));
    var dto = new { forecasts = safePayload.Forecasts };
        var json = System.Text.Json.JsonSerializer.Serialize(dto, domainTypes.JsonSerializerOptions);
        return GzipCompression.CompressString(json);
    }

    public static WeatherForecastProjection Deserialize(DcbDomainTypes domainTypes, ReadOnlySpan<byte> data)
    {
        var json = GzipCompression.DecompressToString(data);
        var node = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json, domainTypes.JsonSerializerOptions);
        var result = new WeatherForecastProjection();
        if (node != null && node.TryGetPropertyValue("forecasts", out var forecastsNode) && forecastsNode is System.Text.Json.Nodes.JsonObject fobj)
        {
            var dict = new Dictionary<Guid, WeatherForecastItem>();
            foreach (var kv in fobj)
            {
                if (Guid.TryParse(kv.Key, out var id) && kv.Value != null)
                {
                    var itemJson = kv.Value!.ToJsonString(domainTypes.JsonSerializerOptions);
                    var item = System.Text.Json.JsonSerializer.Deserialize<WeatherForecastItem>(itemJson, domainTypes.JsonSerializerOptions);
                    if (item != null) dict[id] = item;
                }
            }
            result = result with { Forecasts = dict };
        }
    return result;
    }

    /// <summary>
    ///     Project with tag filtering - only processes events with WeatherForecastTag
    /// </summary>
    public static ResultBox<WeatherForecastProjection> Project(
        WeatherForecastProjection payload,
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
            return ResultBox.FromValue(payload);
        }

        // Get the forecast IDs from the tags
        var forecastIds = weatherForecastTags.Select(tag => tag.ForecastId).ToList();
        
        // Create a copy of the forecasts dictionary for immutability
        var updatedForecasts = new Dictionary<Guid, WeatherForecastItem>(payload.Forecasts);

        // Check if event is within safe window
        var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
    var isEventUnsafe = !eventTime.IsEarlierThanOrEqual(safeWindowThreshold); // retained placeholder; UnsafeForecasts removed

        // Process each affected forecast
        foreach (var forecastId in forecastIds)
        {
            var current = updatedForecasts.TryGetValue(forecastId, out var existing) ? existing : null;
            
            // Process based on event type
            var result = ev.Payload switch
            {
                WeatherForecastCreated created => new WeatherForecastItem(
                    forecastId,
                    created.Location,
                    created.Date.ToDateTime(TimeOnly.MinValue),
                    created.TemperatureC,
                    created.Summary,
                    GetEventTimestamp(ev)),

                WeatherForecastUpdated updated when current != null => current with
                {
                    Location = updated.Location,
                    TemperatureC = updated.TemperatureC,
                    Summary = updated.Summary,
                    LastUpdated = GetEventTimestamp(ev)
                },

                LocationNameChanged locationChanged when current != null => current with
                {
                    Location = locationChanged.NewLocationName,
                    LastUpdated = GetEventTimestamp(ev)
                },

                WeatherForecastDeleted => null, // Delete the item

                _ => current // Unknown event type or can't update non-existent item
            };

            // Update the dictionary
            if (result != null)
            {
                updatedForecasts[forecastId] = result;
                
                // Mark as unsafe if event is within safe window
                // Unsafe tracking removed
            }
            else if (ev.Payload is WeatherForecastDeleted)
            {
                updatedForecasts.Remove(forecastId);
                // Unsafe tracking removed
            }
        }

    return ResultBox.FromValue(payload with { Forecasts = updatedForecasts });
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
    ///     Get all current weather forecasts
    /// </summary>
    public IReadOnlyDictionary<Guid, WeatherForecastItem> GetCurrentForecasts()
    {
        return Forecasts;
    }

}
