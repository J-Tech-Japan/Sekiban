using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Weather;

public record CreateWeatherForecast : ICommandWithHandler<CreateWeatherForecast>
{
    // Allow server-side generation when not provided
    public Guid ForecastId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Location { get; init; } = string.Empty;

    [Required]
    public DateOnly Date { get; init; }

    public TemperatureCelsius TemperatureC { get; init; }

    [StringLength(200)]
    public string? Summary { get; init; }

    public async Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context)
    {
        var id = ForecastId != Guid.Empty ? ForecastId : Guid.CreateVersion7();
        var tag = new WeatherForecastTag(id);
        var exists = await context.TagExistsAsync(tag);

        if (!exists.IsSuccess)
            return ResultBox.Error<EventOrNone>(exists.GetException());

        if (exists.GetValue())
            return ResultBox.Error<EventOrNone>(
                new ApplicationException($"Weather forecast {id} already exists"));

        // Basic range guard for TemperatureC
        var temp = System.Math.Clamp(TemperatureC.ToInt(), -50, 50);
        return EventOrNone.EventWithTags(
            new WeatherForecastCreated(id, Location, Date, temp, Summary),
            tag);
    }
}
