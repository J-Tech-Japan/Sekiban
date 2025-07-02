using Orleans;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Events;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Payloads;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace DaprSekiban.Domain.Aggregates.WeatherForecasts.Commands;

[GenerateSerializer]
public record UpdateWeatherForecastLocationCommand(
    [property: Id(0)] Guid WeatherForecastId,
    [property: Id(1)] string Location) : ICommandWithHandler<UpdateWeatherForecastLocationCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(UpdateWeatherForecastLocationCommand command) => 
        PartitionKeys.Existing<WeatherForecastProjector>(command.WeatherForecastId);

    public ResultBox<EventOrNone> Handle(UpdateWeatherForecastLocationCommand command, ICommandContext<IAggregatePayload> context)
    {
        // Only allow updates on existing WeatherForecast aggregates
        var aggregate = context.GetAggregate();
        if (!aggregate.IsSuccess || aggregate.GetValue().Payload is not WeatherForecast)
        {
            return EventOrNone.None;
        }
        
        return EventOrNone.Event(new WeatherForecastLocationUpdated(command.Location));
    }
}