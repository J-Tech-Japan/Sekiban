using System.Text.Json.Serialization;
using Sekiban.Pure.Events;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;

namespace DaprSample.Domain;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Core Sekiban types
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EmptyAggregatePayload))]
[JsonSerializable(typeof(IMultiProjectorCommon))]
[JsonSerializable(typeof(PartitionKeys))]
[JsonSerializable(typeof(SerializableAggregateListProjector))]
[JsonSerializable(typeof(SerializableAggregate))]
// Event documents
[JsonSerializable(typeof(EventDocument<User.UserCreated>))]
[JsonSerializable(typeof(EventDocument<User.UserNameChanged>))]
[JsonSerializable(typeof(EventDocument<User.UserEmailChanged>))]
// Event payloads
[JsonSerializable(typeof(User.UserCreated))]
[JsonSerializable(typeof(User.UserNameChanged))]
[JsonSerializable(typeof(User.UserEmailChanged))]
// Commands
[JsonSerializable(typeof(User.Commands.CreateUser))]
[JsonSerializable(typeof(User.Commands.UpdateUserName))]
[JsonSerializable(typeof(User.Commands.UpdateUserEmail))]
// Aggregate payloads
[JsonSerializable(typeof(User.User))]
// WeatherForecast Event documents
[JsonSerializable(typeof(EventDocument<Aggregates.WeatherForecasts.Events.WeatherForecastInputted>))]
[JsonSerializable(typeof(EventDocument<Aggregates.WeatherForecasts.Events.WeatherForecastLocationUpdated>))]
[JsonSerializable(typeof(EventDocument<Aggregates.WeatherForecasts.Events.WeatherForecastDeleted>))]
// WeatherForecast Event payloads
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Events.WeatherForecastInputted))]
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Events.WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Events.WeatherForecastDeleted))]
// WeatherForecast Commands
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Commands.InputWeatherForecastCommand))]
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Commands.UpdateWeatherForecastLocationCommand))]
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Commands.DeleteWeatherForecastCommand))]
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Commands.RemoveWeatherForecastCommand))]
// WeatherForecast Aggregate payloads
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Payloads.WeatherForecast))]
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Payloads.DeletedWeatherForecast))]
// WeatherForecast Query and Response
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Queries.WeatherForecastQuery))]
[JsonSerializable(typeof(Aggregates.WeatherForecasts.Queries.WeatherForecastResponse))]
// WeatherForecast Value Objects
[JsonSerializable(typeof(ValueObjects.TemperatureCelsius))]
public partial class DaprSampleEventsJsonContext : JsonSerializerContext;