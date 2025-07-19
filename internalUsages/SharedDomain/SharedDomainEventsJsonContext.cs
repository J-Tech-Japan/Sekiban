using System.Text.Json;
using System.Text.Json.Serialization;
using SharedDomain.Aggregates.User.Commands;
using SharedDomain.Aggregates.User.Events;
using SharedDomain.Aggregates.User.Queries;
using SharedDomain.Aggregates.WeatherForecasts.Commands;
using SharedDomain.Aggregates.WeatherForecasts.Events;
using SharedDomain.Aggregates.WeatherForecasts.Payloads;
using SharedDomain.Aggregates.WeatherForecasts.Queries;
using SharedDomain.ValueObjects;
using Sekiban.Pure.Events;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace SharedDomain;

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
// Projector types
[JsonSerializable(typeof(AggregateListProjector<Aggregates.User.UserProjector>))]
[JsonSerializable(typeof(AggregateListProjector<Aggregates.WeatherForecasts.WeatherForecastProjector>))]
// User domain
[JsonSerializable(typeof(Aggregates.User.User))]
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
public partial class SharedDomainEventsJsonContext : JsonSerializerContext;
