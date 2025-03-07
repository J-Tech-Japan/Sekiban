using Sekiban.Pure.Events;
using System.Text.Json.Serialization;
namespace Pure.Domain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.BranchCreated>))]
[JsonSerializable(typeof(Pure.Domain.BranchCreated))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.BranchNameChanged>))]
[JsonSerializable(typeof(Pure.Domain.BranchNameChanged))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.ClientCreated>))]
[JsonSerializable(typeof(Pure.Domain.ClientCreated))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.ClientNameChanged>))]
[JsonSerializable(typeof(Pure.Domain.ClientNameChanged))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.PaymentProcessedShoppingCart>))]
[JsonSerializable(typeof(Pure.Domain.PaymentProcessedShoppingCart))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.ShoppingCartCreated>))]
[JsonSerializable(typeof(Pure.Domain.ShoppingCartCreated))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.ShoppingCartItemAdded>))]
[JsonSerializable(typeof(Pure.Domain.ShoppingCartItemAdded))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.UserConfirmed>))]
[JsonSerializable(typeof(Pure.Domain.UserConfirmed))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.UserRegistered>))]
[JsonSerializable(typeof(Pure.Domain.UserRegistered))]
[JsonSerializable(typeof(EventDocument<Pure.Domain.UserUnconfirmed>))]
[JsonSerializable(typeof(Pure.Domain.UserUnconfirmed))]
public partial class PureDomainEventsJsonContext : JsonSerializerContext
{
}
