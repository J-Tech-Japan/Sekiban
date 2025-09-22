using Orleans;
namespace Dcb.Domain.Queries;

/// <summary>
/// Result containing weather forecast counts
/// </summary>
[GenerateSerializer]
public record WeatherForecastCountResult(
    [property: Id(0)] int SafeVersion,
    [property: Id(1)] int UnsafeVersion,
    [property: Id(2)] int TotalCount
);