using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Events;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record UpdateWeatherForecastLocationCommand(
    [property: Id(0)] Guid WeatherForecastId,
    [property: Id(1)] string Location) : ICommandWithHandler<UpdateWeatherForecastLocationCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(UpdateWeatherForecastLocationCommand command) => 
        PartitionKeys.Existing<WeatherForecastProjector>(command.WeatherForecastId);

    public ResultBox<EventOrNone> Handle(UpdateWeatherForecastLocationCommand command, ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new WeatherForecastLocationUpdated(command.Location));
}