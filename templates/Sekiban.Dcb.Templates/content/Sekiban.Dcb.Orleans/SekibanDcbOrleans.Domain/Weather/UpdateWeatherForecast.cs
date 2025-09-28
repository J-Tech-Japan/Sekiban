using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record UpdateWeatherForecast : ICommandWithHandler<UpdateWeatherForecast>
{
    [Required]
    public Guid ForecastId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Location { get; init; } = string.Empty;

    [Required]
    public DateOnly Date { get; init; }

    [Required]
    public TemperatureCelsius TemperatureC { get; init; }

    [StringLength(200)]
    public string? Summary { get; init; }

    public static Task<ResultBox<EventOrNone>> HandleAsync(UpdateWeatherForecast command, ICommandContext context) =>
        ResultBox
            .Start
            .Remap(_ => new WeatherForecastTag(command.ForecastId))
            .Combine(context.TagExistsAsync)
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
                        new ApplicationException($"Weather forecast {command.ForecastId} has been deleted"))
                    : ExceptionOrNone.None;
            })
            .Conveyor((tag, _, _) => EventOrNone.EventWithTags(
                new WeatherForecastUpdated(
                    command.ForecastId,
                    command.Location,
                    command.Date,
                    System.Math.Clamp(command.TemperatureC.ToInt(), -50, 50),
                    command.Summary),
                tag));
}
