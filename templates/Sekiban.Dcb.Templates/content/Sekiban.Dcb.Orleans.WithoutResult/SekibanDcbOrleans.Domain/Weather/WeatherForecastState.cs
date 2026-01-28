using Orleans;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Weather;

public record WeatherForecastState : ITagStatePayload
{
    [Id(0)] public Guid ForecastId { get; init; }
    [Id(1)] public string Location { get; init; } = string.Empty;
    [Id(2)] public DateOnly Date { get; init; }
    [Id(3)] public int TemperatureC { get; init; }
    [Id(4)] public string? Summary { get; init; }
    [Id(5)] public bool IsDeleted { get; init; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
