using System.Text.Json.Serialization;

namespace DaprSample.Domain;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(User.UserCreated))]
[JsonSerializable(typeof(User.UserNameChanged))]
[JsonSerializable(typeof(User.UserEmailChanged))]
[JsonSerializable(typeof(User.Commands.CreateUser))]
[JsonSerializable(typeof(User.Commands.UpdateUserName))]
[JsonSerializable(typeof(User.Commands.UpdateUserEmail))]
public partial class DaprSampleEventsJsonContext : JsonSerializerContext;