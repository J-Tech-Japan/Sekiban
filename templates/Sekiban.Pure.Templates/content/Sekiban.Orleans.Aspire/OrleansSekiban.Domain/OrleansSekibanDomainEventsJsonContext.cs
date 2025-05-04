using System.Text.Json.Serialization;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace OrleansSekiban.Domain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(Sekiban.Pure.Aggregates.EmptyAggregatePayload))]
[JsonSerializable(typeof(Sekiban.Pure.Projectors.IMultiProjectorCommon))]
[JsonSerializable(typeof(Sekiban.Pure.Documents.PartitionKeys))]
[JsonSerializable(typeof(Sekiban.Pure.Projectors.SerializableAggregateListProjector))]
[JsonSerializable(typeof(Sekiban.Pure.Aggregates.SerializableAggregate))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events.WeatherForecastDeleted>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events.WeatherForecastDeleted))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events.WeatherForecastInputted>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events.WeatherForecastInputted))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events.WeatherForecastLocationUpdated>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events.WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(OrleansSekiban.Domain.Aggregates.WeatherForecasts.Payloads.WeatherForecast))]
[JsonSerializable(typeof(OrleansSekiban.Domain.Aggregates.WeatherForecasts.Payloads.DeletedWeatherForecast))]
[JsonSerializable(typeof(OrleansSekiban.Domain.Projections.Count.WeatherCountMultiProjection))]
public partial class OrleansSekibanDomainEventsJsonContext : JsonSerializerContext
{
}