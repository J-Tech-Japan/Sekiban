using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Commands;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Queries;
using Sekiban.Pure.Command.Executor;

namespace OrleansSekiban.Web;

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecastQuery.WeatherForecastRecord[]> GetWeatherAsync(int maxItems = 10, string? waitForSortableUniqueId = null, int? pageSize = null, int? pageNumber = null, string? sortBy = null, bool isAsc = false, CancellationToken cancellationToken = default)
    {
        List<WeatherForecastQuery.WeatherForecastRecord>? forecasts = null;
        
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(waitForSortableUniqueId))
            queryParams.Add($"waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}");
        if (pageSize.HasValue)
            queryParams.Add($"pageSize={pageSize.Value}");
        if (pageNumber.HasValue)
            queryParams.Add($"pageNumber={pageNumber.Value}");
        if (!string.IsNullOrEmpty(sortBy))
            queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (isAsc)
            queryParams.Add("isAsc=true");
            
        var requestUri = "/api/weatherforecast";
        if (queryParams.Count > 0)
            requestUri += "?" + string.Join("&", queryParams);
            
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
