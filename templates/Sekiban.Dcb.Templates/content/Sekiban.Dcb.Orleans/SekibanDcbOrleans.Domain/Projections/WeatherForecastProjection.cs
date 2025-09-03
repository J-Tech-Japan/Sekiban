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
///     Weather forecast projection that maintains separate safe and unsafe states
/// </summary>
public record WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{
    public static string MultiProjectorName => nameof(WeatherForecastProjection);
    public static string MultiProjectorVersion => "1.0.0";

    // Safe: Strictly consistent state (only includes safe events)
    // Unsafe: Includes all events (may be ahead but less consistent)
    public Dictionary<Guid, WeatherForecastItem> Forecasts { get; init; } = new();
    public Dictionary<Guid, WeatherForecastItem> UnsafeForecasts { get; init; } = new();

    public static WeatherForecastProjection GenerateInitialPayload() => new();

    public static ResultBox<WeatherForecastProjection> Project(
        WeatherForecastProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        try
        {
            // WeatherForecastTag filter - if no tag, skip
            var forecastTags = tags.OfType<WeatherForecastTag>().ToList();
            if (!forecastTags.Any())
            {
                return ResultBox.FromValue(payload);
            }

            var state = payload with
            {
                Forecasts = new Dictionary<Guid, WeatherForecastItem>(payload.Forecasts),
                UnsafeForecasts = new Dictionary<Guid, WeatherForecastItem>(payload.UnsafeForecasts)
            };

            // Tag-based processing
            foreach (var tag in forecastTags)
            {
                var id = tag.ForecastId;
                switch (ev.Payload)
                {
                    case WeatherForecastCreated created:
                        var newItem = new WeatherForecastItem(
                            created.ForecastId,
                            created.Location,
                            created.Date.ToDateTime(TimeOnly.MinValue),
                            created.TemperatureC,
                            created.Summary,
                            DateTime.UtcNow);
                        state.Forecasts[id] = newItem;
                        state.UnsafeForecasts[id] = newItem;
                        break;
                    case WeatherForecastUpdated updated:
                        if (state.UnsafeForecasts.TryGetValue(id, out var existing))
                        {
                            var updatedItem = existing with
                            {
                                Location = updated.Location,
                                Date = updated.Date.ToDateTime(TimeOnly.MinValue),
                                TemperatureC = updated.TemperatureC,
                                Summary = updated.Summary,
                                LastUpdated = DateTime.UtcNow
                            };
                            state.UnsafeForecasts[id] = updatedItem;
                            state.Forecasts[id] = updatedItem;
                        }
                        break;
                    case WeatherForecastDeleted deleted:
                        state.Forecasts.Remove(id);
                        state.UnsafeForecasts.Remove(id);
                        break;
                    case LocationNameChanged changed:
                        if (state.UnsafeForecasts.TryGetValue(id, out var existingItem))
                        {
                            var changedItem = existingItem with
                            {
                                Location = changed.NewLocationName,
                                LastUpdated = DateTime.UtcNow
                            };
                            state.UnsafeForecasts[id] = changedItem;
                            state.Forecasts[id] = changedItem;
                        }
                        break;
                }
            }
            return ResultBox.FromValue(state);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<WeatherForecastProjection>(ex);
        }
    }

    public IReadOnlyDictionary<Guid, WeatherForecastItem> GetSafeForecasts()
        => Forecasts;
    public IReadOnlyDictionary<Guid, WeatherForecastItem> GetCurrentForecasts()
        => UnsafeForecasts;
}
