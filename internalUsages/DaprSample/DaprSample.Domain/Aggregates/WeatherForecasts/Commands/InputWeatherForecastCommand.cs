using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Events;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using DaprSample.Domain.ValueObjects;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record InputWeatherForecastCommand(
    [property: Id(0)] string Location,
    [property: Id(1)] DateOnly Date,
    [property: Id(2)] TemperatureCelsius TemperatureC,
    [property: Id(3)] string? Summary) : ICommandWithHandlerWithoutLoadingAggregateAsync<InputWeatherForecastCommand, WeatherForecastProjector>
{
    public static Task<EventOrNone<WeatherForecastInputted>> HandleCommandAsync(
        InputWeatherForecastCommand command,
        IServiceProvider serviceProvider) =>
        Task.FromResult(
            EventOrNone<WeatherForecastInputted>.Event(
                new WeatherForecastInputted(
                    command.Location,
                    command.Date,
                    command.TemperatureC,
                    command.Summary)));
}