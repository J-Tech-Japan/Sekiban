using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Text.Json.Serialization;
namespace Pure.Domain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EmptyAggregatePayload))]
[JsonSerializable(typeof(IMultiProjectorCommon))]
[JsonSerializable(typeof(PartitionKeys))]
[JsonSerializable(typeof(SerializableAggregateListProjector))]
[JsonSerializable(typeof(SerializableAggregate))]
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
[JsonSerializable(typeof(EventDocument<UserNameUpdated>))]
[JsonSerializable(typeof(UserNameUpdated))]
[JsonSerializable(typeof(Branch))]
[JsonSerializable(typeof(BuyingShoppingCart))]
[JsonSerializable(typeof(Client))]
[JsonSerializable(typeof(ConfirmedUser))]
[JsonSerializable(typeof(PaymentProcessingShoppingCart))]
[JsonSerializable(typeof(UnconfirmedUser))]
[JsonSerializable(typeof(MultiProjectorPayload))]
public partial class PureDomainEventsJsonContext : JsonSerializerContext
{
}
