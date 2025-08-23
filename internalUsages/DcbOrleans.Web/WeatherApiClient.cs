using Dcb.Domain.Projections;
using Dcb.Domain.Weather;
using System.Text.Json;

namespace DcbOrleans.Web;

public record CommandResponse(bool Success, Guid? EventId, Guid? AggregateId, string? Error, string? SortableUniqueId);

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecastItem[]> GetWeatherAsync(int maxItems = 10, string? waitForSortableUniqueId = null, CancellationToken cancellationToken = default)
    {
        var requestUri = string.IsNullOrEmpty(waitForSortableUniqueId)
            ? "/api/weatherforecast"
            : $"/api/weatherforecast?waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}";
            
        var forecasts = await httpClient.GetFromJsonAsync<List<WeatherForecastItem>>(requestUri, cancellationToken);
        
        if (forecasts == null)
        {
            return [];
        }
        
        // Apply maxItems limit if needed
        if (forecasts.Count > maxItems)
        {
            return forecasts.Take(maxItems).ToArray();
        }
        
        return forecasts.ToArray();
    }

    public async Task<CommandResponse> InputWeatherAsync(CreateWeatherForecast command, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/inputweatherforecast", command, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponse>(cancellationToken) 
               ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }

    public async Task<CommandResponse> RemoveWeatherAsync(Guid weatherForecastId, CancellationToken cancellationToken = default)
    {
        var command = new DeleteWeatherForecast { ForecastId = weatherForecastId };
        var response = await httpClient.PostAsJsonAsync("/api/removeweatherforecast", command, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponse>(cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }

    public async Task<CommandResponse> UpdateLocationAsync(Guid weatherForecastId, string newLocation, CancellationToken cancellationToken = default)
    {
        var command = new ChangeLocationName { ForecastId = weatherForecastId, NewLocationName = newLocation };
        var response = await httpClient.PostAsJsonAsync("/api/updateweatherforecastlocation", command, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponse>(cancellationToken)
               ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }
}