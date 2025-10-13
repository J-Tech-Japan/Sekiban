using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.WithoutResult.Weather;

public record UpdateWeatherForecast : ICommandWithHandlerWithoutResult<UpdateWeatherForecast>
{
    [Required]
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

    public static async Task<EventOrNone> HandleAsync(UpdateWeatherForecast command, ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var exists = (await context.TagExistsAsync(tag)).UnwrapBox();
        if (!exists)
        {
            throw new ApplicationException($"Weather forecast {command.ForecastId} does not exist");
        }

        var state = (await context.GetStateAsync<WeatherForecastProjector>(tag)).UnwrapBox();
        if (state.Payload is WeatherForecastState payload && payload.IsDeleted)
        {
            throw new ApplicationException($"Weather forecast {command.ForecastId} has been deleted");
        }

        return EventOrNone.FromValue(
            new WeatherForecastUpdated(
                command.ForecastId,
                command.Location,
                command.Date,
                command.TemperatureC,
                command.Summary),
            tag);
    }

}
