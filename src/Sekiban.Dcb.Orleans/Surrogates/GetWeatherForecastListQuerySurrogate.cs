using Orleans;
using Dcb.Domain.Queries;

namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
/// Orleans surrogate for GetWeatherForecastListQuery
/// </summary>
[GenerateSerializer]
public record struct GetWeatherForecastListQuerySurrogate(
    [property: Id(0)] bool IncludeDeleted,
    [property: Id(1)] int? PageNumber,
    [property: Id(2)] int? PageSize);
