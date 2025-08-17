using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record DeleteWeatherForecast : ICommandWithHandler<DeleteWeatherForecast>
{
    [Required]
    public Guid ForecastId { get; init; }

    public async Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context)
    {
        var tag = new WeatherForecastTag(ForecastId);
        var exists = await context.TagExistsAsync(tag);
        
        if (!exists.IsSuccess)
            return ResultBox.Error<EventOrNone>(exists.GetException());
            
        if (!exists.GetValue())
            return ResultBox.Error<EventOrNone>(new ApplicationException($"Weather forecast {ForecastId} does not exist"));

        var state = await context.GetStateAsync<WeatherForecastProjector>(tag);
        if (!state.IsSuccess)
            return ResultBox.Error<EventOrNone>(state.GetException());
            
        var payload = state.GetValue().Payload as WeatherForecastState;
        if (payload?.IsDeleted == true)
            return ResultBox.Error<EventOrNone>(new ApplicationException($"Weather forecast {ForecastId} has already been deleted"));

        return EventOrNone.EventWithTags(
            new WeatherForecastDeleted(ForecastId),
            tag);
    }
}