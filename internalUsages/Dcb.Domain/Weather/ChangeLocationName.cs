using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record ChangeLocationName : ICommandWithHandler<ChangeLocationName>
{
    [Required]
    public Guid ForecastId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string NewLocationName { get; init; } = string.Empty;

    public static async Task<ResultBox<EventOrNone>> HandleAsync(ChangeLocationName command, ICommandContext context)
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
                new ApplicationException($"Weather forecast {command.ForecastId} has been deleted"));

        // Check if the location name is actually changing
        if (payload?.Location == command.NewLocationName)
            return EventOrNone.None;

        return EventOrNone.EventWithTags(
            new LocationNameChanged(command.ForecastId, command.NewLocationName, payload?.Location ?? string.Empty),
            tag);
    }
}
