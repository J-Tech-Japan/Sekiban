using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.States.Weather.Deciders;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Decider.Weather;

public record UpdateWeatherForecast : ICommandWithHandler<UpdateWeatherForecast>
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

    public static async Task<EventOrNone> HandleAsync(
        UpdateWeatherForecast command,
        ICommandContext context)
    {
        var tag = new WeatherForecastTag(command.ForecastId);
        var exists = await context.TagExistsAsync(tag);
        if (!exists)
        {
            throw new ApplicationException($"Weather forecast {command.ForecastId} does not exist");
        }

        var state = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(tag);

        // Use Decider.Validate (explicit call to avoid ambiguity)
        WeatherForecastUpdatedDecider.Validate(state.Payload);

        return new WeatherForecastUpdated(
            command.ForecastId,
            command.Location,
            command.Date,
            command.TemperatureC,
            command.Summary).GetEventWithTags();
    }
}
