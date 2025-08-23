using Dcb.Domain.Projections;
using Dcb.Domain.Weather;
using System.Text.Json;

namespace DcbOrleans.Web;

public record CommandResponse(bool Success, Guid? EventId, Guid? AggregateId, string? Error, string? SortableUniqueId);

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecastItem[]> GetWeatherAsync(
        int? pageNumber = null, 
        int? pageSize = null, 
        string? waitForSortableUniqueId = null, 
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        
        if (!string.IsNullOrEmpty(waitForSortableUniqueId))
            queryParams.Add($"waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}");
        
        if (pageNumber.HasValue)
            queryParams.Add($"pageNumber={pageNumber.Value}");
            
        if (pageSize.HasValue)
            queryParams.Add($"pageSize={pageSize.Value}");
        
        var requestUri = queryParams.Count > 0
            ? $"/api/weatherforecast?{string.Join("&", queryParams)}"
            : "/api/weatherforecast";
            
        var forecasts = await httpClient.GetFromJsonAsync<List<WeatherForecastItem>>(requestUri, cancellationToken);
        
        return forecasts?.ToArray() ?? [];
    }
    
    // Overload for backward compatibility
    public async Task<WeatherForecastItem[]> GetWeatherAsync(int maxItems = 10, string? waitForSortableUniqueId = null, CancellationToken cancellationToken = default)
    {
        // Use pagination with the maxItems as pageSize
        return await GetWeatherAsync(pageNumber: 1, pageSize: maxItems, waitForSortableUniqueId: waitForSortableUniqueId, cancellationToken: cancellationToken);
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