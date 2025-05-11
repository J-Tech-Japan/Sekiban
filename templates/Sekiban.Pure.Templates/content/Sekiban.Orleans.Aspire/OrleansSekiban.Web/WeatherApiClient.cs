using OrleansSekiban.Domain;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Commands;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Queries;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;

namespace OrleansSekiban.Web;

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecastQuery.WeatherForecastRecord[]> GetWeatherAsync(int maxItems = 10, string? waitForSortableUniqueId = null, CancellationToken cancellationToken = default)
    {
        List<WeatherForecastQuery.WeatherForecastRecord>? forecasts = null;
        var requestUri = string.IsNullOrEmpty(waitForSortableUniqueId)
            ? "/api/weatherforecast"
            : $"/api/weatherforecast?waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}";
            
        await foreach (var forecast in httpClient.GetFromJsonAsAsyncEnumerable<WeatherForecastQuery.WeatherForecastRecord>(requestUri, cancellationToken))
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

    public async Task<CommandResponseSimple> InputWeatherAsync(InputWeatherForecastCommand command, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/inputweatherforecast", command, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponseSimple>(cancellationToken) 
               ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }

    public async Task<CommandResponseSimple> RemoveWeatherAsync(Guid weatherForecastId, CancellationToken cancellationToken = default)
    {
        var command = new RemoveWeatherForecastCommand(weatherForecastId);
        var response = await httpClient.PostAsJsonAsync("/api/removeweatherforecast", command, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponseSimple>(cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }

    public async Task<CommandResponseSimple> UpdateLocationAsync(Guid weatherForecastId, string newLocation, CancellationToken cancellationToken = default)
    {
        var command = new UpdateWeatherForecastLocationCommand(weatherForecastId, newLocation);
        var response = await httpClient.PostAsJsonAsync("/api/updateweatherforecastlocation", command, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponseSimple>(cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }
}
