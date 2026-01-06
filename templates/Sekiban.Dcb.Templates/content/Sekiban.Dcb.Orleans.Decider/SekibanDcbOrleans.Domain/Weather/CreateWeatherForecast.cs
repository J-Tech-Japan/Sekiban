using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Decider.Weather;

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

    public static async Task<EventOrNone> HandleAsync(
        CreateWeatherForecast command,
        ICommandContext context)
    {
        var forecastId = command.ForecastId != Guid.Empty ? command.ForecastId : Guid.CreateVersion7();
        var tag = new WeatherForecastTag(forecastId);

        var exists = await context.TagExistsAsync(tag);
        if (exists)
        {
            throw new ApplicationException($"Weather forecast {forecastId} already exists");
        }

        return new WeatherForecastCreated(forecastId, command.Location, command.Date, command.TemperatureC, command.Summary)
            .GetEventWithTags();
    }
}
