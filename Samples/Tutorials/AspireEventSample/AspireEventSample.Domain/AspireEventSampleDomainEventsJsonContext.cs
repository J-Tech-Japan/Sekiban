using AspireEventSample.Domain.Aggregates.Branches;
using AspireEventSample.Domain.Aggregates.Carts;
using AspireEventSample.Domain.Projections;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Text.Json.Serialization;
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<BranchCreated>))]
[JsonSerializable(typeof(BranchCreated))]
[JsonSerializable(typeof(EventDocument<BranchNameChanged>))]
[JsonSerializable(typeof(BranchNameChanged))]
[JsonSerializable(typeof(EventDocument<ShoppingCartCreated>))]
[JsonSerializable(typeof(ShoppingCartCreated))]
[JsonSerializable(typeof(EventDocument<ShoppingCartItemAdded>))]
[JsonSerializable(typeof(ShoppingCartItemAdded))]
[JsonSerializable(typeof(EventDocument<ShoppingCartPaymentProcessed>))]
[JsonSerializable(typeof(ShoppingCartPaymentProcessed))]
[JsonSerializable(typeof(EmptyAggregatePayload))]
[JsonSerializable(typeof(IMultiProjectorCommon))]
[JsonSerializable(typeof(PartitionKeys))]
[JsonSerializable(typeof(SerializableAggregateListProjector))]
[JsonSerializable(typeof(BranchMultiProjector))]
// Add missing aggregate payload types
[JsonSerializable(typeof(Branch))]
[JsonSerializable(typeof(BuyingShoppingCart))]
[JsonSerializable(typeof(PaymentProcessingShoppingCart))]
[JsonSerializable(typeof(ShoppingCartItems))]
// Add missing projector types
[JsonSerializable(typeof(AggregateListProjector<BranchProjector>))]
[JsonSerializable(typeof(BranchProjector))]
[JsonSerializable(typeof(AggregateListProjector<ShoppingCartProjector>))]
[JsonSerializable(typeof(ShoppingCartProjector))]
public partial class AspireEventSampleDomainEventsJsonContext : JsonSerializerContext
{
}