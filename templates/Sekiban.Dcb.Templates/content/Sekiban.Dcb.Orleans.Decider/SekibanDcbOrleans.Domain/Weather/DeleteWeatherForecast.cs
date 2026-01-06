using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.States.Weather.Deciders;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Decider.Weather;

public record DeleteWeatherForecast : ICommandWithHandler<DeleteWeatherForecast>
{
    [Required]
    public Guid ForecastId { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        DeleteWeatherForecast command,
        ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var exists = await context.TagExistsAsync(tag);
        if (!exists)
        {
            throw new ApplicationException($"Weather forecast {command.ForecastId} does not exist");
        }

        var state = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(tag);

        // Use Decider.Validate
        WeatherForecastDeletedDecider.Validate(state.Payload);

        return new WeatherForecastDeleted(command.ForecastId).GetEventWithTags();
    }
}
