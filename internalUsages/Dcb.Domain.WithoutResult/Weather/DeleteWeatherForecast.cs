using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.WithoutResult.Weather;

public record DeleteWeatherForecast : ICommandWithHandlerWithoutResult<DeleteWeatherForecast>
{
    [Required]
    public Guid ForecastId { get; init; }

    public static async Task<EventOrNone> HandleAsync(DeleteWeatherForecast command, ICommandContext context)
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
            throw new ApplicationException($"Weather forecast {command.ForecastId} has already been deleted");
        }

        return EventOrNone.FromValue(new WeatherForecastDeleted(command.ForecastId), tag);
    }

}
