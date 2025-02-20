using Sekiban.Pure.Aggregates;

namespace OrleansSekiban.Domain;

public record WeatherForecast(
    string Location,
    DateTime Date,
    int TemperatureC,
    string Summary
) : IAggregatePayload
{
    public int GetTemperatureF() => 32 + (int)(TemperatureC / 0.5556);
}