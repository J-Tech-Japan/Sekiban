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

    public async Task<List<WeatherForecastResponse>?> GetWeatherForecastsAsync(string? waitForSortableUniqueId = null)
    {
        var url = "api/weatherforecast";
        if (!string.IsNullOrEmpty(waitForSortableUniqueId))
        {
            url += $"?waitForSortableUniqueId={waitForSortableUniqueId}";
        }
        
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
        var response = await _httpClient.PostAsJsonAsync("api/weatherforecast/input", command);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<CommandResponseSimple>();
        }
        return null;
    }

    public async Task<CommandResponseSimple?> UpdateLocationAsync(Guid weatherForecastId, string location)
    {
        var command = new UpdateWeatherForecastLocationCommand(weatherForecastId, location);
        var response = await _httpClient.PostAsJsonAsync($"api/weatherforecast/{weatherForecastId}/update-location", new { Location = location });
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
        var response = await _httpClient.PostAsync($"api/weatherforecast/{weatherForecastId}/remove", null);
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