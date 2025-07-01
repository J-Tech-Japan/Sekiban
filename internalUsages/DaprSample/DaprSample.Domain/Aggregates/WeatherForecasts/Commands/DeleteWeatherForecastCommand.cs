using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Events;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record DeleteWeatherForecastCommand : ICommandWithHandler<DeleteWeatherForecastCommand, WeatherForecastProjector, WeatherForecast>
{
    public static EventOrNone<WeatherForecastDeleted> HandleCommand(
        DeleteWeatherForecastCommand command,
        WeatherForecast state) =>
        EventOrNone<WeatherForecastDeleted>.Event(WeatherForecastDeleted.Instance);
}