using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShippingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Commands;
using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Testing.Story;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.Abstracts;

public abstract class AggregateSubtypeTypeR : TestBase
{
    private readonly IDocumentRemover _documentRemover;
    private readonly HybridStoreManager _hybridStoreManager;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCache _memoryCache;
    private readonly IAggregateLoader aggregateLoader;
    private readonly Guid cartId = Guid.NewGuid();
    private readonly ICommandExecutor commandExecutor;
    private readonly IMultiProjectionService multiProjectionService;
    private CommandExecutorResponseWithEvents commandResponse = default!;
    public AggregateSubtypeTypeR(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, testOutputHelper, providerGenerator)
    {
        _documentRemover = GetService<IDocumentRemover>();
        commandExecutor = GetService<ICommandExecutor>();
        aggregateLoader = GetService<IAggregateLoader>();
        multiProjectionService = GetService<IMultiProjectionService>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _memoryCache = GetService<IMemoryCache>();
    }

    [Fact(DisplayName = "SubtypeのAggregateを作成する")]
    public async Task CosmosDbStory()
    {
        // 先に全データを削除する
        await _documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Default);
        await _documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Dissolvable);
        await _documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Default);
        await _documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Dissolvable);

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartR { CartId = cartId, Code = "TEST1", Name = "Name1", Quantity = 1 });
        Assert.Equal(1, commandResponse.Version);
        Assert.Single(commandResponse.Events);
        Assert.Equal(nameof(CartAggregateR), commandResponse.Events.First().AggregateType);


        // this time, the aggregate is created as ShoppingCartI
        var aggregateAsCart = await aggregateLoader.AsAggregateAsync<CartAggregateR>(cartId);
        Assert.NotNull(aggregateAsCart);
        Assert.True(aggregateAsCart.GetPayloadTypeIs<ShoppingCartR>());
        var aggregateAsCartState = aggregateAsCart.ToState();
        Assert.NotNull(aggregateAsCartState);
        Assert.True(aggregateAsCartState.Payload is ShoppingCartR);
        var aggregateAsShoppingCart = await aggregateLoader.AsAggregateAsync<ShoppingCartR>(cartId);
        Assert.NotNull(aggregateAsShoppingCart);
        Assert.True(aggregateAsShoppingCart.GetPayloadTypeIs<ShoppingCartR>());
        var aggregateAsShoppingCartState = aggregateAsShoppingCart.ToState();
        Assert.NotNull(aggregateAsShoppingCartState);
        var aggregateAsCartState2 = aggregateAsShoppingCart.ToState<CartAggregateR>();
        Assert.NotNull(aggregateAsCartState2);
        Assert.True(aggregateAsCartState2.Payload is ShoppingCartR);

        // Parent interface can get state as child interface
        var aggregateAsCartState3 = await aggregateLoader.AsDefaultStateAsync<CartAggregateR>(cartId);
        Assert.NotNull(aggregateAsCartState3);
        Assert.Equal(typeof(ShoppingCartR), aggregateAsCartState3.Payload.GetType());
        var aggregateAsShoppingCartState2 = await aggregateLoader.AsDefaultStateAsync<ShoppingCartR>(cartId);
        Assert.NotNull(aggregateAsShoppingCartState2);
        // This time, aggregate payload is ShippingCartI, so it will return null
        var aggregateAsPurchasedCartState = await aggregateLoader.AsDefaultStateAsync<PurchasedCartR>(cartId);
        Assert.Null(aggregateAsPurchasedCartState);



    }
    [Fact]
    public async Task SecondCommandTest()
    {
        await CosmosDbStory();
        var purchasedTime = DateTime.Now;
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderR { CartId = cartId, OrderSubmittedLocalTime = purchasedTime, ReferenceVersion = commandResponse.Version });


        // this time, the aggregate is created as ShoppingCartI
        var aggregateAsCart = await aggregateLoader.AsAggregateAsync<CartAggregateR>(cartId);
        Assert.NotNull(aggregateAsCart);
        Assert.True(aggregateAsCart.GetPayloadTypeIs<PurchasedCartR>());
        var aggregateAsCartState = aggregateAsCart.ToState();
        Assert.NotNull(aggregateAsCartState);
        Assert.True(aggregateAsCartState.Payload is PurchasedCartR);
        Assert.Equal(purchasedTime, aggregateAsCart.ToState<PurchasedCartR>().Payload.PurchasedDate);

        var aggregateAsShoppingCart = await aggregateLoader.AsAggregateAsync<ShoppingCartR>(cartId);
        Assert.NotNull(aggregateAsShoppingCart);
        Assert.False(aggregateAsShoppingCart.GetPayloadTypeIs<ShoppingCartR>());

        var aggregateAsCartState2 = aggregateAsShoppingCart.ToState<CartAggregateR>();
        Assert.NotNull(aggregateAsCartState2);
        Assert.True(aggregateAsCartState2.Payload is PurchasedCartR);

        var aggregateAsPurchasedCart = await aggregateLoader.AsAggregateAsync<PurchasedCartR>(cartId);
        Assert.NotNull(aggregateAsPurchasedCart);
        Assert.True(aggregateAsPurchasedCart.GetPayloadTypeIs<PurchasedCartR>());

        var aggregateAsCartState3 = aggregateAsShoppingCart.ToState<CartAggregateR>();
        Assert.NotNull(aggregateAsCartState3);
        Assert.True(aggregateAsCartState3.Payload is PurchasedCartR);

    }

    [Fact]
    public async Task SimpleCommandsTest()
    {
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartR { CartId = cartId, Code = "TEST1", Name = "Name1", Quantity = 1 });
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartR { CartId = cartId, Code = "TEST2", Name = "Name2", Quantity = 1 });
        var purchasedTime = DateTime.Now;
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderR { CartId = cartId, OrderSubmittedLocalTime = purchasedTime, ReferenceVersion = commandResponse.Version });

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReceivePaymentToPurchasedCartR
            {
                CartId = cartId,
                PaymentMethod = "Credit Card",
                Amount = 1000,
                Currency = "USD",
                ReferenceVersion = commandResponse.Version
            });

        var state = await aggregateLoader.AsDefaultStateAsync<CartAggregateR>(cartId);
        Assert.NotNull(state);
        Assert.Equal(typeof(ShippingCartR), state.Payload.GetType());
    }

    [Fact]
    public async Task SimpleCommandsTestSnapshot()
    {
        // 先に全データを削除する
        await _documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Default);
        await _documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Dissolvable);
        await _documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Default);
        await _documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Dissolvable);

        var snapshotCartId = Guid.NewGuid();
        for (var i = 0; i < 140; i++)
        {
            commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartR { CartId = snapshotCartId, Code = $"TEST{i:000}", Name = $"Name{i:000}", Quantity = i + 1 });
            var state = await aggregateLoader.AsDefaultStateAsync<CartAggregateR>(snapshotCartId);
            Assert.NotNull(state);
            Assert.Equal(typeof(ShoppingCartR).Name, state.PayloadTypeName);
        }
        // Remove in memory data
        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache).Compact(1);

        var stateAfter = await aggregateLoader.AsDefaultStateAsync<CartAggregateR>(snapshotCartId);
        Assert.NotNull(stateAfter);
        Assert.Equal(typeof(ShoppingCartR).Name, stateAfter.PayloadTypeName);
    }


    [Fact]
    public async Task AfterChangePayloadType()
    {
        await SecondCommandTest();

        await Assert.ThrowsAnyAsync<Exception>(
            async () =>
            {
                commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
                    new AddItemToShoppingCartR { CartId = cartId, Code = "TEST2", Name = "Name2", Quantity = 2 });
            });

    }

    [Fact]
    public async Task multiProjectionsTest()
    {
        // 先に全データを削除する
        await _documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Default);
        await _documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Dissolvable);
        await _documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Default);
        await _documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Dissolvable);

        var cartId1 = Guid.NewGuid();
        var cartId2 = Guid.NewGuid();
        var cartId3 = Guid.NewGuid();
        var cartId4 = Guid.NewGuid();

        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartR { CartId = cartId1, Name = "Name1", Code = "Code1", Quantity = 1 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartR { CartId = cartId1, Name = "Name2", Code = "Code2", Quantity = 2 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new SubmitOrderR { CartId = cartId1, OrderSubmittedLocalTime = DateTime.Now, ReferenceVersion = commandResponse.Version })
            .Result;


        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartR { CartId = cartId2, Name = "Name2", Code = "Code2", Quantity = 1 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new SubmitOrderR { CartId = cartId2, OrderSubmittedLocalTime = DateTime.Now, ReferenceVersion = commandResponse.Version })
            .Result;

        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartR { CartId = cartId3, Name = "Name3", Code = "Code3", Quantity = 1 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartR { CartId = cartId3, Name = "Name2", Code = "Code2", Quantity = 2 })
            .Result;

        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartR { CartId = cartId4, Name = "Name4", Code = "Code4", Quantity = 1 })
            .Result;

        var list = await multiProjectionService.GetAggregateList<CartAggregateR>();
        Assert.Equal(4, list.Count);
        Assert.Equal(2, list.Count(m => m.Payload is ShoppingCartR));
        Assert.Equal(2, list.Count(m => m.Payload is PurchasedCartR));

    }
}
