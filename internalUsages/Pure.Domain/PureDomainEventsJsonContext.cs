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


    public class PureDomainMultiProjectorTypes2 : IMultiProjectorTypes
    {
        public ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, IEvent ev)
            => multiProjector switch
            {
                Pure.Domain.MultiProjectorPayload multiProjectorPayload => multiProjectorPayload.Project(multiProjectorPayload, ev)
                    .Remap(mp => (IMultiProjectorCommon)mp),
                AggregateListProjector<Pure.Domain.BranchProjector> branchProjector => branchProjector.Project(branchProjector, ev)
                    .Remap(mp => (IMultiProjectorCommon)mp),
                AggregateListProjector<Pure.Domain.ShoppingCartProjector> shoppingCartProjector => shoppingCartProjector.Project(shoppingCartProjector, ev)
                    .Remap(mp => (IMultiProjectorCommon)mp),
                AggregateListProjector<Pure.Domain.UserProjector> userProjector => userProjector.Project(userProjector, ev)
                    .Remap(mp => (IMultiProjectorCommon)mp),
                AggregateListProjector<Pure.Domain.ClientProjector> clientProjector => clientProjector.Project(clientProjector, ev)
                    .Remap(mp => (IMultiProjectorCommon)mp),
                _ => new ApplicationException(multiProjector.GetType().Name)
            };

        public ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, IReadOnlyList<IEvent> events) => ResultBox.FromValue(events.ToList())
            .ReduceEach(multiProjector, (ev, common) => Project(common, ev));

        public IMultiProjectorStateCommon ToTypedState(MultiProjectionState state)
            => state.ProjectorCommon switch
            {
                Pure.Domain.MultiProjectorPayload projector => new MultiProjectionState<Pure.Domain.MultiProjectorPayload>(projector, state.LastEventId, state.LastSortableUniqueId, state.Version, state.AppliedSnapshotVersion, state.RootPartitionKey),
                AggregateListProjector<Pure.Domain.BranchProjector> aggregator => new MultiProjectionState<AggregateListProjector<Pure.Domain.BranchProjector>>(aggregator, state.LastEventId, state.LastSortableUniqueId, state.Version, state.AppliedSnapshotVersion, state.RootPartitionKey),
                AggregateListProjector<Pure.Domain.ShoppingCartProjector> aggregator => new MultiProjectionState<AggregateListProjector<Pure.Domain.ShoppingCartProjector>>(aggregator, state.LastEventId, state.LastSortableUniqueId, state.Version, state.AppliedSnapshotVersion, state.RootPartitionKey),
                AggregateListProjector<Pure.Domain.UserProjector> aggregator => new MultiProjectionState<AggregateListProjector<Pure.Domain.UserProjector>>(aggregator, state.LastEventId, state.LastSortableUniqueId, state.Version, state.AppliedSnapshotVersion, state.RootPartitionKey),
                AggregateListProjector<Pure.Domain.ClientProjector> aggregator => new MultiProjectionState<AggregateListProjector<Pure.Domain.ClientProjector>>(aggregator, state.LastEventId, state.LastSortableUniqueId, state.Version, state.AppliedSnapshotVersion, state.RootPartitionKey),
                _ => throw new ArgumentException($"No state type found for projector type: {state.ProjectorCommon.GetType().Name}")
            };

        public IMultiProjectorCommon GetProjectorFromMultiProjectorName(string grainName)
            => grainName switch
            {
                not null when Pure.Domain.MultiProjectorPayload.GetMultiProjectorName() == grainName => Pure.Domain.MultiProjectorPayload.GenerateInitialPayload(),
                not null when AggregateListProjector<Pure.Domain.BranchProjector>.GetMultiProjectorName() == grainName => AggregateListProjector<Pure.Domain.BranchProjector>.GenerateInitialPayload(),
                not null when AggregateListProjector<Pure.Domain.ShoppingCartProjector>.GetMultiProjectorName() == grainName => AggregateListProjector<Pure.Domain.ShoppingCartProjector>.GenerateInitialPayload(),
                not null when AggregateListProjector<Pure.Domain.UserProjector>.GetMultiProjectorName() == grainName => AggregateListProjector<Pure.Domain.UserProjector>.GenerateInitialPayload(),
                not null when AggregateListProjector<Pure.Domain.ClientProjector>.GetMultiProjectorName() == grainName => AggregateListProjector<Pure.Domain.ClientProjector>.GenerateInitialPayload(),
                _ => throw new ArgumentException($"No projector found for grain name: {grainName}")
            };
        public ResultBox<string> GetMultiProjectorNameFromMultiProjector(IMultiProjectorCommon multiProjector)
            => multiProjector switch
            {
                Pure.Domain.MultiProjectorPayload projector => ResultBox.FromValue(Pure.Domain.MultiProjectorPayload.GetMultiProjectorName()),
                AggregateListProjector<Pure.Domain.BranchProjector> aggregator => ResultBox.FromValue(AggregateListProjector<Pure.Domain.BranchProjector>.GetMultiProjectorName()),
                AggregateListProjector<Pure.Domain.ShoppingCartProjector> aggregator => ResultBox.FromValue(AggregateListProjector<Pure.Domain.ShoppingCartProjector>.GetMultiProjectorName()),
                AggregateListProjector<Pure.Domain.UserProjector> aggregator => ResultBox.FromValue(AggregateListProjector<Pure.Domain.UserProjector>.GetMultiProjectorName()),
                AggregateListProjector<Pure.Domain.ClientProjector> aggregator => ResultBox.FromValue(AggregateListProjector<Pure.Domain.ClientProjector>.GetMultiProjectorName()),
                _ => ResultBox<string>.Error(new ApplicationException(multiProjector.GetType().Name))
            };

        public List<Type> GetMultiProjectorTypes()
        {
            var types = new List<Type>();
            // Add multi-projector types
            types.Add(typeof(Pure.Domain.MultiProjectorPayload));
            return types;
        }
        public async Task<ResultBox<string>> GetSerialisedMultiProjector(IMultiProjectorCommon multiProjector, SekibanDomainTypes domainTypes) =>
        multiProjector switch
        {
            Pure.Domain.MultiProjectorPayload projector => ResultBox.FromValue(JsonSerializer.Serialize(projector, domainTypes.JsonSerializerOptions)),
            AggregateListProjector<Pure.Domain.BranchProjector> aggregator => await SerializableAggregateListProjector.SerializeAggregateList(aggregator, domainTypes),
            AggregateListProjector<Pure.Domain.ShoppingCartProjector> aggregator => await SerializableAggregateListProjector.SerializeAggregateList(aggregator, domainTypes),
            AggregateListProjector<Pure.Domain.UserProjector> aggregator => await SerializableAggregateListProjector.SerializeAggregateList(aggregator, domainTypes),
            AggregateListProjector<Pure.Domain.ClientProjector> aggregator => await SerializableAggregateListProjector.SerializeAggregateList(aggregator, domainTypes),
            _ => ResultBox<string>.Error(new ApplicationException(multiProjector.GetType().Name))
        };
        public async Task<ResultBox<IMultiProjectorCommon>> GetSerialisedMultiProjector(
            string json,
            string typeFullName,
            SekibanDomainTypes domainTypes) => typeFullName switch
        {
            "Sekiban.Pure.MultiProjectorPayload" => ResultBox
                .FromValue(
                    JsonSerializer.Deserialize<Pure.Domain.MultiProjectorPayload>(
                        json,
                        domainTypes.JsonSerializerOptions))
                .Remap(mp => (IMultiProjectorCommon)mp),
            "Sekiban.Pure.Projectors.AggregateListProjector`1[[Pure.Domain.BranchProjector, Pure.Domain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]" => 
                await SerializableAggregateListProjector.DeserializeAggregateList<BranchProjector>(json, domainTypes).Remap(mp => (IMultiProjectorCommon)mp),
            //followoing other projectors
            _ => ResultBox<IMultiProjectorCommon>.Error(new ApplicationException(typeFullName))
        };
    }
