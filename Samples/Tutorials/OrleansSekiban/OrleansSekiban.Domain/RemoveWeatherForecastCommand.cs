using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain;

[GenerateSerializer]
public record RemoveWeatherForecastCommand(Guid WeatherForecastId) : ICommandWithHandler<RemoveWeatherForecastCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(RemoveWeatherForecastCommand command) => 
        PartitionKeys.Existing<WeatherForecastProjector>(command.WeatherForecastId);

    public ResultBox<EventOrNone> Handle(RemoveWeatherForecastCommand command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new WeatherForecastDeleted());    
}
