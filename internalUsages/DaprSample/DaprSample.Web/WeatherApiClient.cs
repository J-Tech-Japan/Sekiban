using System.Net.Http.Json;
using DaprSample.Domain;
using DaprSample.Domain.Aggregates.WeatherForecasts.Commands;
using DaprSample.Domain.ValueObjects;
using Sekiban.Pure.Command.Executor;

namespace DaprSample.Web;

public class WeatherApiClient
{
    private readonly HttpClient httpClient;
    private readonly ILogger<WeatherApiClient> logger;

    public WeatherApiClient(HttpClient httpClient, ILogger<WeatherApiClient> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<List<WeatherForecastResponse>?> GetWeatherForecastsAsync()
    {
        try
        {
            var result = await httpClient.GetFromJsonAsync<ListQueryResult<WeatherForecastQuery.Record>>("/api/weatherforecast");
            return result?.Items.Select(item => new WeatherForecastResponse
            {
                WeatherForecastId = item.AggregateId,
                Location = item.Forecast.Location,
                Date = item.Forecast.Date,
                TemperatureC = item.Forecast.TemperatureC,
                Summary = item.Forecast.Summary,
                TemperatureF = item.Forecast.TemperatureC.ToFahrenheit()
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get weather forecasts");
            return new List<WeatherForecastResponse>();
        }
    }

    public async Task<SimpleCommandResponse?> AddWeatherForecastAsync(string location, DateTime date, int temperatureC, string? summary)
    {
        var command = new InputWeatherForecastCommand(
            location,
            DateOnly.FromDateTime(date),
            new TemperatureCelsius(temperatureC),
            summary
        );
        return await httpClient.PostAsJsonAsync("/api/weatherforecast/input", command)
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<SimpleCommandResponse>())
            .Unwrap();
    }

    public async Task<SimpleCommandResponse?> UpdateLocationAsync(Guid aggregateId, string location)
    {
        var request = new { Location = location };
        return await httpClient.PostAsJsonAsync($"/api/weatherforecast/{aggregateId}/update-location", request)
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<SimpleCommandResponse>())
            .Unwrap();
    }

    public async Task<SimpleCommandResponse?> RemoveForecastAsync(Guid aggregateId)
    {
        // First delete, then remove
        await httpClient.PostAsync($"/api/weatherforecast/{aggregateId}/delete", null);
        return await httpClient.PostAsync($"/api/weatherforecast/{aggregateId}/remove", null)
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<SimpleCommandResponse>())
            .Unwrap();
    }

    public async Task<GenerateDataResponse?> GenerateSampleDataAsync()
    {
        return await httpClient.PostAsync("/api/weatherforecast/generate", null)
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<GenerateDataResponse>())
            .Unwrap();
    }
}

public class WeatherForecastResponse
{
    public Guid WeatherForecastId { get; set; }
    public string Location { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TemperatureCelsius TemperatureC { get; set; } = new(0);
    public int TemperatureF { get; set; }
    public string? Summary { get; set; }
}

public class GenerateDataResponse
{
    public string Message { get; set; } = string.Empty;
    public int Count { get; set; }
}