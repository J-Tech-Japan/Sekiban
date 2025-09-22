using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record CreateWeatherForecast : ICommandWithHandler<CreateWeatherForecast>
{
    public Guid ForecastId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Location { get; init; } = string.Empty;

    [Required]
    public DateOnly Date { get; init; }

    [Range(-50, 50)]
    public int TemperatureC { get; init; }

    [StringLength(200)]
    public string? Summary { get; init; }

    public static Task<ResultBox<EventOrNone>> HandleAsync(CreateWeatherForecast command, ICommandContext context) =>
        ResultBox
            .Start
            .Remap(_ =>
            {
                var forecastId = command.ForecastId != Guid.Empty ? command.ForecastId : Guid.CreateVersion7();
                return (ForecastId: forecastId, Tag: new WeatherForecastTag(forecastId));
            })
            .Combine(state => context.TagExistsAsync(state.Tag))
            .Verify((state, exists) =>
                exists
                    ? ExceptionOrNone.FromException(new ApplicationException($"Weather forecast {state.ForecastId} already exists"))
                    : ExceptionOrNone.None)
            .Conveyor((state, _) => EventOrNone.EventWithTags(
                new WeatherForecastCreated(state.ForecastId, command.Location, command.Date, command.TemperatureC, command.Summary),
                state.Tag));
}
