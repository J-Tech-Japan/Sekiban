using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Events;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record DeleteWeatherForecastCommand(
    [property: Id(0)] Guid WeatherForecastId
) : ICommandWithHandler<DeleteWeatherForecastCommand, WeatherForecastProjector, WeatherForecast>
{
    public PartitionKeys SpecifyPartitionKeys(DeleteWeatherForecastCommand command) => 
        PartitionKeys.Existing<WeatherForecastProjector>(command.WeatherForecastId);

    public ResultBox<EventOrNone> Handle(DeleteWeatherForecastCommand command, ICommandContext<WeatherForecast> context) =>
        EventOrNone.Event(WeatherForecastDeleted.Instance);
}