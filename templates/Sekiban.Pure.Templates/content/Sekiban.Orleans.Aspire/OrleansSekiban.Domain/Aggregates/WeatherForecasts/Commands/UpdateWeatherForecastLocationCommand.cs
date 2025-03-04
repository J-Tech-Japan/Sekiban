using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record UpdateWeatherForecastLocationCommand(
    Guid WeatherForecastId,
    string NewLocation
) : ICommandWithHandler<UpdateWeatherForecastLocationCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(UpdateWeatherForecastLocationCommand command) => 
        PartitionKeys.Existing<WeatherForecastProjector>(command.WeatherForecastId);

    public ResultBox<EventOrNone> Handle(UpdateWeatherForecastLocationCommand command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new WeatherForecastLocationUpdated(command.NewLocation));    
}
