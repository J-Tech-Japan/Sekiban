using Orleans;
using Dcb.Domain.Queries;

namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
/// Orleans surrogate converter for GetWeatherForecastListQuery
/// </summary>
[RegisterConverter]
public sealed class GetWeatherForecastListQuerySurrogateConverter : IConverter<GetWeatherForecastListQuery, GetWeatherForecastListQuerySurrogate>
{
    public GetWeatherForecastListQuery ConvertFromSurrogate(in GetWeatherForecastListQuerySurrogate surrogate) =>
        new()
        {
            IncludeDeleted = surrogate.IncludeDeleted,
            PageNumber = surrogate.PageNumber,
            PageSize = surrogate.PageSize
        };

    public GetWeatherForecastListQuerySurrogate ConvertToSurrogate(in GetWeatherForecastListQuery value) =>
        new(value.IncludeDeleted, value.PageNumber, value.PageSize);
}
