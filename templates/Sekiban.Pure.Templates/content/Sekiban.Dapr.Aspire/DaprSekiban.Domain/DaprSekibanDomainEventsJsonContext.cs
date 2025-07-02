using System.Text.Json;
using System.Text.Json.Serialization;
using DaprSekiban.Domain.Aggregates.User.Commands;
using DaprSekiban.Domain.Aggregates.User.Events;
using DaprSekiban.Domain.Aggregates.User.Queries;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Commands;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Events;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Payloads;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Queries;
using DaprSekiban.Domain.ValueObjects;
using Sekiban.Pure.Events;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace DaprSekiban.Domain;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    WriteIndented = true,
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
[JsonSerializable(typeof(IEvent))]
[JsonSerializable(typeof(TemperatureCelsius))]
// User domain
[JsonSerializable(typeof(DaprSekiban.Domain.Aggregates.User.User))]
[JsonSerializable(typeof(CreateUser))]
[JsonSerializable(typeof(UpdateUserName))]
[JsonSerializable(typeof(UpdateUserEmail))]
[JsonSerializable(typeof(UserCreated))]
[JsonSerializable(typeof(UserNameChanged))]
[JsonSerializable(typeof(UserEmailChanged))]
[JsonSerializable(typeof(UserListQuery))]
[JsonSerializable(typeof(UserQuery))]
[JsonSerializable(typeof(UserStatisticsQuery))]
// WeatherForecast domain
[JsonSerializable(typeof(WeatherForecast))]
[JsonSerializable(typeof(DeletedWeatherForecast))]
[JsonSerializable(typeof(InputWeatherForecastCommand))]
[JsonSerializable(typeof(UpdateWeatherForecastLocationCommand))]
[JsonSerializable(typeof(DeleteWeatherForecastCommand))]
[JsonSerializable(typeof(RemoveWeatherForecastCommand))]
[JsonSerializable(typeof(WeatherForecastInputted))]
[JsonSerializable(typeof(WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(WeatherForecastQuery))]
[JsonSerializable(typeof(WeatherForecastResponse))]
[JsonSerializable(typeof(ListQueryResult<WeatherForecastResponse>))]
// Event documents for User
[JsonSerializable(typeof(EventDocument<UserCreated>))]
[JsonSerializable(typeof(EventDocument<UserNameChanged>))]
[JsonSerializable(typeof(EventDocument<UserEmailChanged>))]
// Event documents for WeatherForecast
[JsonSerializable(typeof(EventDocument<WeatherForecastInputted>))]
[JsonSerializable(typeof(EventDocument<WeatherForecastLocationUpdated>))]
[JsonSerializable(typeof(EventDocument<WeatherForecastDeleted>))]
public partial class DaprSekibanDomainEventsJsonContext : JsonSerializerContext;