using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record RemoveWeatherForecastCommand : ICommandWithHandlerRemovable<RemoveWeatherForecastCommand, WeatherForecastProjector, DeletedWeatherForecast>
{
    public static CommandRemovableResponse HandleCommand(
        RemoveWeatherForecastCommand command,
        DeletedWeatherForecast state) =>
        CommandRemovableResponse.Remove();
}