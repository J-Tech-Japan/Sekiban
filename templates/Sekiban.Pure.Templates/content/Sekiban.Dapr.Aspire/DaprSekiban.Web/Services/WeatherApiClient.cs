using System.Net.Http.Json;
using System.Text.Json;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Commands;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Queries;
using DaprSekiban.Domain.ValueObjects;
using Sekiban.Pure.Command.Executor;

namespace DaprSekiban.Web.Services;

public class WeatherApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WeatherApiClient> _logger;

    public WeatherApiClient(HttpClient httpClient, ILogger<WeatherApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<WeatherForecastResponse>?> GetWeatherForecastsAsync(string? waitForSortableUniqueId = null, int? pageSize = null, int? pageNumber = null, string? sortBy = null, bool isAsc = false)
    {
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
            
        var url = "api/weatherforecast";
        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);
        
        try
        {
            _logger.LogInformation("=== MAKING API REQUEST ===");
            _logger.LogInformation("HttpClient BaseAddress: {BaseAddress}", _httpClient.BaseAddress);
            _logger.LogInformation("Request URL: {Url}", url);
            _logger.LogInformation("Full Request URL: {FullUrl}", new Uri(_httpClient.BaseAddress!, url));
            
            var response = await _httpClient.GetFromJsonAsync<List<WeatherForecastResponse>>(url);
            _logger.LogInformation("API request successful");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error making API request to {Url}", url);
            throw;
        }
    }

    public async Task<CommandResponseSimple?> AddWeatherForecastAsync(InputWeatherForecastCommand command)
    {
        var response = await _httpClient.PostAsJsonAsync("api/inputweatherforecast", command);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<CommandResponseSimple>();
        }
        return null;
    }

    public async Task<CommandResponseSimple?> UpdateLocationAsync(Guid weatherForecastId, string location)
    {
        var command = new UpdateWeatherForecastLocationCommand(weatherForecastId, location);
        var response = await _httpClient.PostAsJsonAsync("api/updateweatherforecastlocation", command);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<CommandResponseSimple>();
        }
        return null;
    }

    public async Task<CommandResponseSimple?> DeleteWeatherForecastAsync(Guid weatherForecastId)
    {
        var response = await _httpClient.PostAsync($"api/weatherforecast/{weatherForecastId}/delete", null);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<CommandResponseSimple>();
        }
        return null;
    }

    public async Task<CommandResponseSimple?> RemoveWeatherForecastAsync(Guid weatherForecastId)
    {
        var command = new RemoveWeatherForecastCommand(weatherForecastId);
        var response = await _httpClient.PostAsJsonAsync("api/removeweatherforecast", command);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<CommandResponseSimple>();
        }
        return null;
    }

    public async Task<GenerateDataResponse?> GenerateSampleDataAsync()
    {
        var response = await _httpClient.PostAsync("api/weatherforecast/generate", null);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<GenerateDataResponse>();
        }
        return null;
    }
}

public record GenerateDataResponse(string Message, int Count);