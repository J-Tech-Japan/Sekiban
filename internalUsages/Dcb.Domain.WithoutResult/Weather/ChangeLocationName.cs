using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.WithoutResult.Weather;

public record ChangeLocationName : ICommandWithHandlerWithoutResult<ChangeLocationName>
{
    [Required]
    public Guid ForecastId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string NewLocationName { get; init; } = string.Empty;

    public static async Task<EventOrNone> HandleAsync(ChangeLocationName command, ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var exists = (await context.TagExistsAsync(tag)).UnwrapBox();
        if (!exists)
        {
            throw new ApplicationException($"Weather forecast {command.ForecastId} does not exist");
        }

        var state = (await context.GetStateAsync<WeatherForecastProjector>(tag)).UnwrapBox();
        if (state.Payload is WeatherForecastState payload)
        {
            if (payload.IsDeleted)
            {
                throw new ApplicationException($"Weather forecast {command.ForecastId} has been deleted");
            }

            if (payload.Location == command.NewLocationName)
            {
                return EventOrNone.Empty;
            }

            return EventOrNone.FromValue(
                new LocationNameChanged(
                    command.ForecastId,
                    command.NewLocationName,
                    payload.Location),
                tag);
        }

        throw new ApplicationException("Weather forecast state is invalid");
    }

}
