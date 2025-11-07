using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Weather;

public record WeatherForecastState : ITagStatePayload
{
    public Guid ForecastId { get; init; }
    public string Location { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public int TemperatureC { get; init; }
    public string? Summary { get; init; }
    public bool IsDeleted { get; init; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
