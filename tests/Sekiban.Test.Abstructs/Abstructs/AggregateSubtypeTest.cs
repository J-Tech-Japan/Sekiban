using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShippingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Command;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Abstructs.Abstructs;

public abstract class AggregateSubtypeTest : TestBase<FeatureCheckDependency>
{
    private readonly Guid cartId = Guid.NewGuid();
    private readonly ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor;
    private CommandExecutorResponseWithEvents commandResponse = default!;

    public AggregateSubtypeTest(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(
        sekibanTestFixture,
        testOutputHelper,
        providerGenerator) =>
        singleProjectionSnapshotAccessor = GetService<ISingleProjectionSnapshotAccessor>();

    [Fact]
    public async Task CreateTheSubtypesAggregate()
    {
        RemoveAllFromDefaultAndDissolvable();

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId, Code = "TEST1", Name = "Name1", Quantity = 1 });
        Assert.Equal(1, commandResponse.Version);
        Assert.Single(commandResponse.Events);
        Assert.Equal(nameof(ICartAggregate), commandResponse.Events.First().AggregateType);


        // this time, the aggregate is created as ShoppingCartI
        var aggregateAsCart = await aggregateLoader.AsAggregateAsync<ICartAggregate>(cartId);
        Assert.NotNull(aggregateAsCart);
        Assert.True(aggregateAsCart.GetPayloadTypeIs<ShoppingCartI>());
        var aggregateAsCartState = aggregateAsCart.ToState();
        Assert.NotNull(aggregateAsCartState);
        Assert.True(aggregateAsCartState.Payload is ShoppingCartI);
        var aggregateAsShoppingCart = await aggregateLoader.AsAggregateAsync<ShoppingCartI>(cartId);
        Assert.NotNull(aggregateAsShoppingCart);
        Assert.True(aggregateAsShoppingCart.GetPayloadTypeIs<ShoppingCartI>());
        var aggregateAsShoppingCartState = aggregateAsShoppingCart.ToState();
        Assert.NotNull(aggregateAsShoppingCartState);
        var aggregateAsCartState2 = aggregateAsShoppingCart.ToState<ICartAggregate>();
        Assert.NotNull(aggregateAsCartState2);
        Assert.True(aggregateAsCartState2.Payload is ShoppingCartI);

        // Parent interface can get state as child interface
        var aggregateAsCartState3 = await aggregateLoader.AsDefaultStateAsync<ICartAggregate>(cartId);
        Assert.NotNull(aggregateAsCartState3);
        Assert.Equal(typeof(ShoppingCartI), aggregateAsCartState3.Payload.GetType());
        var aggregateAsShoppingCartState2 = await aggregateLoader.AsDefaultStateAsync<ShoppingCartI>(cartId);
        Assert.NotNull(aggregateAsShoppingCartState2);
        // This time, aggregate payload is ShippingCartI, so it will return null
        var aggregateAsPurchasedCartState = await aggregateLoader.AsDefaultStateAsync<PurchasedCartI>(cartId);
        Assert.Null(aggregateAsPurchasedCartState);



    }
    [Fact]
    public async Task SecondCommandTest()
    {
        await CreateTheSubtypesAggregate();
        var purchasedTime = DateTime.Now;
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderI
            {
                CartId = cartId, OrderSubmittedLocalTime = purchasedTime, ReferenceVersion = commandResponse.Version
            });


        // this time, the aggregate is created as ShoppingCartI
        var aggregateAsCart = await aggregateLoader.AsAggregateAsync<ICartAggregate>(cartId);
        Assert.NotNull(aggregateAsCart);
        Assert.True(aggregateAsCart.GetPayloadTypeIs<PurchasedCartI>());
        var aggregateAsCartState = aggregateAsCart.ToState();
        Assert.NotNull(aggregateAsCartState);
        Assert.True(aggregateAsCartState.Payload is PurchasedCartI);
        Assert.Equal(purchasedTime, aggregateAsCart.ToState<PurchasedCartI>().Payload.PurchasedDate);

        var aggregateAsShoppingCart = await aggregateLoader.AsAggregateAsync<ShoppingCartI>(cartId);
        Assert.NotNull(aggregateAsShoppingCart);
        Assert.False(aggregateAsShoppingCart.GetPayloadTypeIs<ShoppingCartI>());

        var aggregateAsCartState2 = aggregateAsShoppingCart.ToState<ICartAggregate>();
        Assert.NotNull(aggregateAsCartState2);
        Assert.True(aggregateAsCartState2.Payload is PurchasedCartI);

        var aggregateAsPurchasedCart = await aggregateLoader.AsAggregateAsync<PurchasedCartI>(cartId);
        Assert.NotNull(aggregateAsPurchasedCart);
        Assert.True(aggregateAsPurchasedCart.GetPayloadTypeIs<PurchasedCartI>());

        var aggregateAsCartState3 = aggregateAsShoppingCart.ToState<ICartAggregate>();
        Assert.NotNull(aggregateAsCartState3);
        Assert.True(aggregateAsCartState3.Payload is PurchasedCartI);

    }

    [Fact]
    public async Task SimpleCommandsTest()
    {
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId, Code = "TEST1", Name = "Name1", Quantity = 1 });
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId, Code = "TEST2", Name = "Name2", Quantity = 1 });
        var purchasedTime = DateTime.Now;
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderI
            {
                CartId = cartId, OrderSubmittedLocalTime = purchasedTime, ReferenceVersion = commandResponse.Version
            });

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReceivePaymentToPurchasedCartI
            {
                CartId = cartId,
                PaymentMethod = "Credit Card",
                Amount = 1000,
                Currency = "USD",
                ReferenceVersion = commandResponse.Version
            });

        var state = await aggregateLoader.AsDefaultStateAsync<ICartAggregate>(cartId);
        Assert.NotNull(state);
        Assert.Equal(typeof(ShippingCartI), state.Payload.GetType());
    }

    [Fact]
    public async Task SimpleCommandsTestSnapshot()
    {
        RemoveAllFromDefaultAndDissolvable();

        var snapshotCartId = Guid.NewGuid();
        for (var i = 0; i < 140; i++)
        {
            commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartI
                    { CartId = snapshotCartId, Code = $"TEST{i:000}", Name = $"Name{i:000}", Quantity = i + 1 });
            var state = await aggregateLoader.AsDefaultStateAsync<ICartAggregate>(snapshotCartId);
            Assert.NotNull(state);
            Assert.Equal(nameof(ShoppingCartI), state.PayloadTypeName);
        }

        var cart1 = await aggregateLoader.AsDefaultStateFromInitialAsync<ICartAggregate>(snapshotCartId, toVersion: 90);
        var cartSnapshot = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(cart1!);
        await documentPersistentWriter.SaveSingleSnapshotAsync(
            cartSnapshot!,
            new AggregateWriteStream(typeof(ICartAggregate)),
            false);
        var cart2 = await aggregateLoader.AsDefaultStateFromInitialAsync<ICartAggregate>(snapshotCartId);
        var cartSnapshot2 = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(cart2!);
        await documentPersistentWriter.SaveSingleSnapshotAsync(
            cartSnapshot2!,
            new AggregateWriteStream(typeof(ICartAggregate)),
            true);

        var snapshots = await documentPersistentRepository.GetSnapshotsForAggregateAsync(
            snapshotCartId,
            typeof(ICartAggregate),
            typeof(ICartAggregate));

        Assert.Contains(cartSnapshot!.Id, snapshots.Select(m => m.Id));
        var clientFromSnapshot = snapshots.First(m => m.Id == cartSnapshot.Id).GetState();
        Assert.NotNull(clientFromSnapshot);

        Assert.Contains(cartSnapshot2!.Id, snapshots.Select(m => m.Id));
        var clientFromSnapshot2 = snapshots.First(m => m.Id == cartSnapshot2.Id).GetState();
        Assert.NotNull(clientFromSnapshot2);



        ResetInMemoryDocumentStoreAndCache();

        snapshots = await documentPersistentRepository.GetSnapshotsForAggregateAsync(
            snapshotCartId,
            typeof(ICartAggregate),
            typeof(ICartAggregate));
        Assert.NotEmpty(snapshots);
        foreach (var snapshot in snapshots)
        {
            var state = snapshot.GetState()?.GetPayload() as ICartAggregate;
            Assert.NotNull(state);
        }
        var stateAfter = await aggregateLoader.AsDefaultStateAsync<ICartAggregate>(snapshotCartId);
        Assert.NotNull(stateAfter);
        Assert.Equal(nameof(ShoppingCartI), stateAfter.PayloadTypeName);
        Assert.NotEqual(0, stateAfter.AppliedSnapshotVersion);
    }


    [Fact]
    public async Task AfterChangePayloadType()
    {
        await SecondCommandTest();

        await Assert.ThrowsAnyAsync<Exception>(
            async () =>
            {
                commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
                    new AddItemToShoppingCartI { CartId = cartId, Code = "TEST2", Name = "Name2", Quantity = 2 });
            });

    }

    [Fact]
    public async Task CanAddPaymentAfterSubmit()
    {
        await SecondCommandTest();

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReceivePaymentToPurchasedCartI
                { CartId = cartId, Amount = 1000, Currency = "JPY", ReferenceVersion = commandResponse.Version });
    }

    [Fact]
    public async Task MultiProjectionsTest()
    {
        RemoveAllFromDefaultAndDissolvable();

        var cartId1 = Guid.NewGuid();
        var cartId2 = Guid.NewGuid();
        var cartId3 = Guid.NewGuid();
        var cartId4 = Guid.NewGuid();

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId1, Name = "Name1", Code = "Code1", Quantity = 1 });
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId1, Name = "Name2", Code = "Code2", Quantity = 2 });
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderI
            {
                CartId = cartId1, OrderSubmittedLocalTime = DateTime.Now, ReferenceVersion = commandResponse.Version
            });


        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId2, Name = "Name2", Code = "Code2", Quantity = 1 });
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderI
            {
                CartId = cartId2, OrderSubmittedLocalTime = DateTime.Now, ReferenceVersion = commandResponse.Version
            });

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId3, Name = "Name3", Code = "Code3", Quantity = 1 });
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId3, Name = "Name2", Code = "Code2", Quantity = 2 });

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI { CartId = cartId4, Name = "Name4", Code = "Code4", Quantity = 1 });

        var list = await multiProjectionService.GetAggregateList<ICartAggregate>();
        Assert.Equal(4, list.Count);
        Assert.Equal(2, list.Count(m => m.Payload is ShoppingCartI));
        Assert.Equal(2, list.Count(m => m.Payload is PurchasedCartI));

    }
}
