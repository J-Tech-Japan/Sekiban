using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record DeleteWeatherForecast : ICommandWithHandler<DeleteWeatherForecast>
{
    [Required]
    public Guid ForecastId { get; init; }

    public static Task<ResultBox<EventOrNone>> HandleAsync(DeleteWeatherForecast command, ICommandContext context) =>
        ResultBox
            .Start
            .Remap(_ => new WeatherForecastTag(command.ForecastId))
            .Combine(tag => context.TagExistsAsync(tag))
            .Verify((tag, exists) => exists
                ? ExceptionOrNone.None
                : ExceptionOrNone.FromException(
                    new ApplicationException($"Weather forecast {command.ForecastId} does not exist")))
            .Combine((tag, _) => context.GetStateAsync<WeatherForecastProjector>(tag))
            .Verify((_, _, state) =>
            {
                var payload = state.Payload as WeatherForecastState;
                return payload?.IsDeleted == true
                    ? ExceptionOrNone.FromException(
                        new ApplicationException($"Weather forecast {command.ForecastId} has already been deleted"))
                    : ExceptionOrNone.None;
            })
            .Conveyor((tag, _, _) => EventOrNone.EventWithTags(new WeatherForecastDeleted(command.ForecastId), tag));

}
