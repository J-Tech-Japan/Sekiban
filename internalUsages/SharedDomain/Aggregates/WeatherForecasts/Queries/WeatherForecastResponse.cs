using SharedDomain.Aggregates.WeatherForecasts.Payloads;
namespace SharedDomain.Aggregates.WeatherForecasts.Queries;

[GenerateSerializer]
public record WeatherForecastResponse(
    [property: Id(0)] Guid WeatherForecastId,
    [property: Id(1)] string Location,
    [property: Id(2)] DateOnly Date,
    [property: Id(3)] int TemperatureC,
    [property: Id(4)] string? Summary,
    [property: Id(5)] int TemperatureF
)
{
    public static WeatherForecastResponse FromWeatherForecast(Guid id, WeatherForecast forecast)
    {
        return new WeatherForecastResponse(
            id,
            forecast.Location,
            forecast.Date,
            forecast.TemperatureC.Value,
            forecast.Summary,
            32 + (int)(forecast.TemperatureC.Value / 0.5556)
        );
    }
}