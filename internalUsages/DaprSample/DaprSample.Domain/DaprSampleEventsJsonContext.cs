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
public partial class DaprSampleEventsJsonContext : JsonSerializerContext;