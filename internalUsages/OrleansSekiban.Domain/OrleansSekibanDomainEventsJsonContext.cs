using System.Text.Json.Serialization;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;
using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<WeatherForecastInputted>))]
[JsonSerializable(typeof(WeatherForecastInputted))]
[JsonSerializable(typeof(EventDocument<WeatherForecastDeleted>))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(EventDocument<WeatherForecastLocationUpdated>))]
[JsonSerializable(typeof(WeatherForecastLocationUpdated))]
public partial class OrleansSekibanDomainEventsJsonContext : JsonSerializerContext
{
}
