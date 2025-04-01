using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Text.Json.Serialization;
namespace Pure.Domain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<BranchCreated>))]
[JsonSerializable(typeof(BranchCreated))]
[JsonSerializable(typeof(EventDocument<BranchNameChanged>))]
[JsonSerializable(typeof(BranchNameChanged))]
[JsonSerializable(typeof(EventDocument<ClientCreated>))]
[JsonSerializable(typeof(ClientCreated))]
[JsonSerializable(typeof(EventDocument<ClientNameChanged>))]
[JsonSerializable(typeof(ClientNameChanged))]
[JsonSerializable(typeof(EventDocument<PaymentProcessedShoppingCart>))]
[JsonSerializable(typeof(PaymentProcessedShoppingCart))]
[JsonSerializable(typeof(EventDocument<ShoppingCartCreated>))]
[JsonSerializable(typeof(ShoppingCartCreated))]
[JsonSerializable(typeof(EventDocument<ShoppingCartItemAdded>))]
[JsonSerializable(typeof(ShoppingCartItemAdded))]
[JsonSerializable(typeof(EventDocument<UserConfirmed>))]
[JsonSerializable(typeof(UserConfirmed))]
[JsonSerializable(typeof(EventDocument<UserRegistered>))]
[JsonSerializable(typeof(UserRegistered))]
[JsonSerializable(typeof(EventDocument<UserUnconfirmed>))]
[JsonSerializable(typeof(UserUnconfirmed))]
[JsonSerializable(typeof(Branch))]
[JsonSerializable(typeof(Client))]
[JsonSerializable(typeof(ConfirmedUser))]
[JsonSerializable(typeof(EmptyAggregatePayload))]
[JsonSerializable(typeof(UnconfirmedUser))]
[JsonSerializable(typeof(SerializableAggregate))]
[JsonSerializable(typeof(SerializableAggregateListProjector))]
public partial class PureDomainEventsJsonContext : JsonSerializerContext
{
}
