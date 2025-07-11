using System.Net.Http.Json;
using System.Text.Json;
using SharedDomain.Aggregates.WeatherForecasts.Commands;
using SharedDomain.Aggregates.WeatherForecasts.Queries;
using SharedDomain.ValueObjects;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Query;

namespace DaprSample.Web.Services;

public class WeatherApiClient
{
    private readonly HttpClient _httpClient;

    public WeatherApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<WeatherForecastResponse>?> GetWeatherForecastsAsync(string? waitForSortableUniqueId = null)
    {
        var url = "api/weatherforecast";
        if (!string.IsNullOrEmpty(waitForSortableUniqueId))
        {
            url += $"?waitForSortableUniqueId={waitForSortableUniqueId}";
        }
        var response = await _httpClient.GetFromJsonAsync<ListQueryResult<WeatherForecastResponse>>(url);
        return response?.Items?.ToList();
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
        var request = new { Location = location };
        var response = await _httpClient.PostAsJsonAsync($"api/weatherforecast/{weatherForecastId}/update-location", request);
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

    public async Task<List<CommandResponseSimple>?> GenerateSampleDataAsync()
    {
        var response = await _httpClient.PostAsync("api/weatherforecast/generate", null);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<CommandResponseSimple>>();
        }
        return null;
    }
}