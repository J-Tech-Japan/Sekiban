using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Pure.Domain;
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(EventDocumentCommon))]
    [JsonSerializable(typeof(EventDocumentCommon[]))]
    [JsonSerializable(typeof(Sekiban.Pure.Aggregates.EmptyAggregatePayload))]
    [JsonSerializable(typeof(Sekiban.Pure.Projectors.IMultiProjectorCommon))]
    [JsonSerializable(typeof(Sekiban.Pure.Documents.PartitionKeys))]
    [JsonSerializable(typeof(Sekiban.Pure.Projectors.SerializableAggregateListProjector))]
    [JsonSerializable(typeof(Sekiban.Pure.Aggregates.SerializableAggregate))]
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
    [JsonSerializable(typeof(Pure.Domain.Branch))]
    [JsonSerializable(typeof(Pure.Domain.BuyingShoppingCart))]
    [JsonSerializable(typeof(Pure.Domain.Client))]
    [JsonSerializable(typeof(Pure.Domain.ConfirmedUser))]
    [JsonSerializable(typeof(Pure.Domain.PaymentProcessingShoppingCart))]
    [JsonSerializable(typeof(Pure.Domain.UnconfirmedUser))]
    [JsonSerializable(typeof(Pure.Domain.MultiProjectorPayload))]
    public partial class PureDomainEventsJsonContext : JsonSerializerContext
    {
    }