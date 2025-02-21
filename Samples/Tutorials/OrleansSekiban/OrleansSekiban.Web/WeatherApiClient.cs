using OrleansSekiban.Domain;

namespace OrleansSekiban.Web;

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecast[]> GetWeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default)
    {
        List<WeatherForecast>? forecasts = null;

        await foreach (var forecast in httpClient.GetFromJsonAsAsyncEnumerable<WeatherForecast>("/api/weatherforecast", cancellationToken))
        {
            if (forecasts?.Count >= maxItems)
            {
                break;
            }
            if (forecast is not null)
            {
                forecasts ??= [];
                forecasts.Add(forecast);
            }
        }

        return forecasts?.ToArray() ?? [];
    }

    public async Task InputWeatherAsync(InputWeatherForecastCommand command, CancellationToken cancellationToken = default)
    {
        await httpClient.PostAsJsonAsync("/api/inputweatherforecast", command, cancellationToken);
    }
}
