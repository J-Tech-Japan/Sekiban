using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using SharedDomain.Aggregates.WeatherForecasts.Events;
using SharedDomain.Aggregates.WeatherForecasts.Payloads;
namespace SharedDomain.Aggregates.WeatherForecasts;

[GenerateSerializer]
public class WeatherForecastProjector : IAggregateProjector
{
    public Type[] PayloadTypes => new[]
    {
        typeof(WeatherForecast),
        typeof(DeletedWeatherForecast)
    };

    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) =>
        (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, WeatherForecastInputted inputted) => new WeatherForecast
            {
                Location = inputted.Location,
                Date = inputted.Date,
                TemperatureC = inputted.TemperatureC,
                Summary = inputted.Summary
            },
            (WeatherForecast forecast, WeatherForecastLocationUpdated locationUpdated) => forecast with
            {
                Location = locationUpdated.Location
            },
            (WeatherForecast forecast, WeatherForecastDeleted) => DeletedWeatherForecast.Instance,
            _ => payload
        };
}
