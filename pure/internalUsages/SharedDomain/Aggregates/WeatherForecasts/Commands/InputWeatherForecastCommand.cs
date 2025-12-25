using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using SharedDomain.Aggregates.WeatherForecasts.Events;
using SharedDomain.ValueObjects;
namespace SharedDomain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record InputWeatherForecastCommand(
    [property: Id(0)]
    string Location,
    [property: Id(1)]
    DateOnly Date,
    [property: Id(2)]
    TemperatureCelsius TemperatureC,
    [property: Id(3)]
    string? Summary) : ICommandWithHandlerAsync<InputWeatherForecastCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(InputWeatherForecastCommand command) =>
        PartitionKeys.Generate<WeatherForecastProjector>();

    public Task<ResultBox<EventOrNone>> HandleAsync(
        InputWeatherForecastCommand command,
        ICommandContext<IAggregatePayload> context) =>
        Task.FromResult(
            EventOrNone.Event(
                new WeatherForecastInputted(command.Location, command.Date, command.TemperatureC, command.Summary)));
}
