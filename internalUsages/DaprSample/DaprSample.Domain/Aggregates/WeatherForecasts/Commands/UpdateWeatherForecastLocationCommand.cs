using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Events;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record UpdateWeatherForecastLocationCommand(
    [property: Id(0)] string Location) : ICommandWithHandler<UpdateWeatherForecastLocationCommand, WeatherForecastProjector, WeatherForecast>
{
    public static EventOrNone<WeatherForecastLocationUpdated> HandleCommand(
        UpdateWeatherForecastLocationCommand command,
        WeatherForecast state) =>
        EventOrNone<WeatherForecastLocationUpdated>.Event(
            new WeatherForecastLocationUpdated(command.Location));
}