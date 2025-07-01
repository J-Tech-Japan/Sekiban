using System.Net.Http.Json;
using System.Text.Json;
using DaprSample.Domain.Aggregates.WeatherForecasts.Commands;
using DaprSample.Domain.Aggregates.WeatherForecasts.Queries;
using DaprSample.Domain.ValueObjects;
using Sekiban.Pure.Command.Executor;

namespace DaprSample.Web.Services;

public class WeatherApiClient
{
    private readonly HttpClient _httpClient;

    public WeatherApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<WeatherForecastResponse>?> GetWeatherForecastsAsync()
    {
        var response = await _httpClient.GetFromJsonAsync<List<object>>("api/weatherforecast");
        if (response == null) return null;

        // Convert the response to WeatherForecastResponse objects
        var forecasts = new List<WeatherForecastResponse>();
        foreach (var item in response)
        {
            if (item is System.Text.Json.JsonElement jsonElement)
            {
                var forecast = jsonElement.Deserialize<WeatherForecastResponse>();
                if (forecast != null)
                {
                    forecasts.Add(forecast);
                }
            }
        }
        return forecasts;
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