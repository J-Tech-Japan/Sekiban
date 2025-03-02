using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Aggregates.Carts;
using Sekiban.Pure.Events;
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
public partial class AspireEventSampleApiServiceEventsJsonContext : JsonSerializerContext
{
}