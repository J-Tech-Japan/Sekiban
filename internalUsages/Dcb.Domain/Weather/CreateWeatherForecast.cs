using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;

namespace Dcb.Domain.Weather;

public record CreateWeatherForecast : ICommandWithHandler<CreateWeatherForecast>
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

    public async Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context)
    {
        var tag = new WeatherForecastTag(ForecastId);
        var exists = await context.TagExistsAsync(tag);
        
        if (!exists.IsSuccess)
            return ResultBox.Error<EventOrNone>(exists.GetException());
            
        if (exists.GetValue())
            return ResultBox.Error<EventOrNone>(new ApplicationException($"Weather forecast {ForecastId} already exists"));

        return EventOrNone.EventWithTags(
            new WeatherForecastCreated(ForecastId, Location, Date, TemperatureC, Summary),
            tag);
    }
}