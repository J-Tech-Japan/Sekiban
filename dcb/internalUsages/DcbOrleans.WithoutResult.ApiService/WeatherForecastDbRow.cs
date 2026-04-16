namespace DcbOrleans.WithoutResult.ApiService;

internal static class WeatherForecastDbProjection
{
    public const string LogicalTable = "forecasts";
}

public sealed class WeatherForecastDbRow
{
    public Guid ForecastId { get; set; }
    public string Location { get; set; } = string.Empty;
    public DateTime ForecastDate { get; set; }
    public int TemperatureC { get; set; }
    public string? Summary { get; set; }
    public bool IsDeleted { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public DateTimeOffset LastAppliedAt { get; set; }
}
