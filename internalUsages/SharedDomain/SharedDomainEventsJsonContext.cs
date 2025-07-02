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

namespace SharedDomain;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Serialization,
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IEvent))]
[JsonSerializable(typeof(TemperatureCelsius))]
// User domain
[JsonSerializable(typeof(SharedDomain.Aggregates.User.User))]
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
public partial class SharedDomainEventsJsonContext : JsonSerializerContext;