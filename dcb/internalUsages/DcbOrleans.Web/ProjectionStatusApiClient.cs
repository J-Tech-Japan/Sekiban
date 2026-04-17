namespace DcbOrleans.Web;

public class ProjectionStatusApiClient(HttpClient httpClient)
{
    public Task<MultiProjectionStatusDto?> GetStandardWeatherProjectionStatusAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<MultiProjectionStatusDto>("/api/weatherforecast/status", cancellationToken);

    public Task<MultiProjectionStatusDto?> GetSingleWeatherProjectionStatusAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<MultiProjectionStatusDto>("/api/weatherforecastsingle/status", cancellationToken);

    public Task<MultiProjectionStatusDto?> GetGenericWeatherProjectionStatusAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<MultiProjectionStatusDto>("/api/weatherforecastgeneric/status", cancellationToken);

    public Task<WeatherForecastDbStatusDto?> GetWeatherDatabaseProjectionStatusAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<WeatherForecastDbStatusDto>("/api/weatherforecastdb/status", cancellationToken);
}

public sealed class MultiProjectionStatusDto
{
    public string ProjectorName { get; set; } = string.Empty;
    public bool IsSubscriptionActive { get; set; }
    public bool IsCaughtUp { get; set; }
    public string? CurrentPosition { get; set; }
    public long EventsProcessed { get; set; }
    public DateTime? LastEventTime { get; set; }
    public DateTime? LastPersistTime { get; set; }
    public long StateSize { get; set; }
    public long SafeStateSize { get; set; }
    public long UnsafeStateSize { get; set; }
    public bool HasError { get; set; }
    public string? LastError { get; set; }
}

public sealed class WeatherForecastDbStatusDto
{
    public string? DatabaseType { get; set; }
    public string? Table { get; set; }
    public MaterializedViewStatusDto? Status { get; set; }
    public MvRegistryEntryDto? Entry { get; set; }
    public List<MvRegistryEntryDto> Entries { get; set; } = [];
}

public sealed class MaterializedViewStatusDto
{
    public string ServiceId { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public int ViewVersion { get; set; }
    public bool Started { get; set; }
    public bool CatchUpInProgress { get; set; }
    public bool SubscriptionActive { get; set; }
    public int BufferedEventCount { get; set; }
    public string? CurrentPosition { get; set; }
    public string? LastReceivedSortableUniqueId { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastCatchUpStartedAt { get; set; }
    public DateTimeOffset? LastCatchUpCompletedAt { get; set; }
}

public sealed class MvRegistryEntryDto
{
    public string ServiceId { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public int ViewVersion { get; set; }
    public string LogicalTable { get; set; } = string.Empty;
    public string PhysicalTable { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? CurrentPosition { get; set; }
    public string? TargetPosition { get; set; }
    public string? LastSortableUniqueId { get; set; }
    public long AppliedEventVersion { get; set; }
    public string? LastAppliedSource { get; set; }
    public DateTimeOffset? LastAppliedAt { get; set; }
    public string? LastStreamReceivedSortableUniqueId { get; set; }
    public DateTimeOffset? LastStreamReceivedAt { get; set; }
    public string? LastStreamAppliedSortableUniqueId { get; set; }
    public string? LastCatchUpSortableUniqueId { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public string? Metadata { get; set; }
}
