using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record DeleteWeatherForecast : ICommandWithHandler<DeleteWeatherForecast>
{
    [Required]
    public Guid ForecastId { get; init; }

    public static async Task<ResultBox<EventOrNone>> HandleAsync(DeleteWeatherForecast command, ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var exists = await context.TagExistsAsync(tag);

        if (!exists.IsSuccess)
            return ResultBox.Error<EventOrNone>(exists.GetException());

        if (!exists.GetValue())
            return ResultBox.Error<EventOrNone>(
                new ApplicationException($"Weather forecast {command.ForecastId} does not exist"));

        var state = await context.GetStateAsync<WeatherForecastProjector>(tag);
        if (!state.IsSuccess)
            return ResultBox.Error<EventOrNone>(state.GetException());

        var payload = state.GetValue().Payload as WeatherForecastState;
        if (payload?.IsDeleted == true)
            return ResultBox.Error<EventOrNone>(
                new ApplicationException($"Weather forecast {command.ForecastId} has already been deleted"));

        return EventOrNone.EventWithTags(new WeatherForecastDeleted(command.ForecastId), tag);
    }
}
