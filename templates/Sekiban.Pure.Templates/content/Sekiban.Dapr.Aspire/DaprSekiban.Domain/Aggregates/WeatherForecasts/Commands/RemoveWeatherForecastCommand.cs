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
public record RemoveWeatherForecastCommand([property: Id(0)] Guid WeatherForecastId) : ICommandWithHandler<RemoveWeatherForecastCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(RemoveWeatherForecastCommand command) => 
        PartitionKeys.Existing<WeatherForecastProjector>(command.WeatherForecastId);

    public ResultBox<EventOrNone> Handle(RemoveWeatherForecastCommand command, ICommandContext<IAggregatePayload> context)
    {
        // Only allow removal on existing WeatherForecast aggregates (not already deleted)
        var aggregate = context.GetAggregate();
        if (!aggregate.IsSuccess || aggregate.GetValue().Payload is not WeatherForecast)
        {
            return EventOrNone.None;
        }
        
        return EventOrNone.Event(new WeatherForecastDeleted());
    }
}