using System.Text.Json.Serialization;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;
using OrleansSekiban.Domain.ValueObjects;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace OrleansSekiban.Domain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EmptyAggregatePayload))]
[JsonSerializable(typeof(WeatherForecast))]
[JsonSerializable(typeof(TemperatureCelsius))]
[JsonSerializable(typeof(EventDocument<WeatherForecastInputted>))]
[JsonSerializable(typeof(WeatherForecastInputted))]
[JsonSerializable(typeof(EventDocument<WeatherForecastDeleted>))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(EventDocument<WeatherForecastLocationUpdated>))]
[JsonSerializable(typeof(WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(IMultiProjectorCommon))]
[JsonSerializable(typeof(MultiProjectionState<>))]
[JsonSerializable(typeof(Dictionary<string, Aggregate>))]
[JsonSerializable(typeof(Aggregate))]
[JsonSerializable(typeof(AggregateListProjector<WeatherForecastProjector>))]
[JsonSerializable(typeof(MultiProjectionState<AggregateListProjector<WeatherForecastProjector>>))]
public partial class OrleansSekibanDomainEventsJsonContext : JsonSerializerContext
{
}
