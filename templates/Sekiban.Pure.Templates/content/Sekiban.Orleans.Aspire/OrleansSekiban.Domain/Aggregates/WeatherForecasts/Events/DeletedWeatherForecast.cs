using Sekiban.Pure.Aggregates;

namespace OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record DeletedWeatherForecast(
    string Location,
    DateOnly Date,
    int TemperatureC,
    string Summary
) : IAggregatePayload
{
    public int GetTemperatureF()
    {
        return 32 + (int)(TemperatureC / 0.5556);
    }
}
