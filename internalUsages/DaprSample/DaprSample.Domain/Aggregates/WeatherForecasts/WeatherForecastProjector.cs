using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Events;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace DaprSample.Domain.Aggregates.WeatherForecasts;

[GenerateSerializer]
public class WeatherForecastProjector : ISingleProjector<WeatherForecast, DeletedWeatherForecast>
{
    public IAggregatePayload? Project(Event ev, IAggregatePayload? currentState) =>
        (ev.Payload, currentState) switch
        {
            (WeatherForecastInputted inputted, _) =>
                new WeatherForecast
                {
                    Location = inputted.Location,
                    Date = inputted.Date,
                    TemperatureC = inputted.TemperatureC,
                    Summary = inputted.Summary
                },
            (WeatherForecastLocationUpdated locationUpdated, WeatherForecast state) =>
                state with { Location = locationUpdated.Location },
            (WeatherForecastDeleted, _) => DeletedWeatherForecast.Instance,
            _ => currentState
        };
}