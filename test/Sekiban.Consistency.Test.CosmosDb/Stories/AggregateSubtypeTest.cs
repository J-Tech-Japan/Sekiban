using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShippingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts.Commands;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Infrastructure.Cosmos;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class AggregateSubtypeTest : TestBase
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly IAggregateLoader aggregateLoader;
    private readonly Guid cartId = Guid.NewGuid();
    private readonly ICommandExecutor commandExecutor;
    private readonly IMultiProjectionService multiProjectionService;
    private CommandExecutorResponseWithEvents commandResponse = default!;
    public AggregateSubtypeTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper)
    {
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        commandExecutor = GetService<ICommandExecutor>();
        aggregateLoader = GetService<IAggregateLoader>();
        multiProjectionService = GetService<IMultiProjectionService>();
    }

    [Fact(DisplayName = "SubtypeのAggregateを作成する")]
    public async Task CosmosDbStory()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.Command,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);



        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI
                { CartId = cartId, Code = "TEST1", Name = "Name1", Quantity = 1 });
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
        await CosmosDbStory();
        var purchasedTime = DateTime.Now;
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderI
                { CartId = cartId, OrderSubmittedLocalTime = purchasedTime, ReferenceVersion = commandResponse.Version });


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
            new AddItemToShoppingCartI
                { CartId = cartId, Code = "TEST1", Name = "Name1", Quantity = 1 });
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new AddItemToShoppingCartI
                { CartId = cartId, Code = "TEST2", Name = "Name2", Quantity = 1 });
        var purchasedTime = DateTime.Now;
        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new SubmitOrderI
                { CartId = cartId, OrderSubmittedLocalTime = purchasedTime, ReferenceVersion = commandResponse.Version });

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReceivePaymentToPurchasedCartI
                { CartId = cartId, PaymentMethod = "Credit Card", Amount = 1000, Currency = "USD", ReferenceVersion = commandResponse.Version });

        var state = await aggregateLoader.AsDefaultStateAsync<ICartAggregate>(cartId);
        Assert.NotNull(state);
        Assert.Equal(typeof(ShippingCartI), state.Payload.GetType());
    }

    [Fact]
    public async Task AfterChangePayloadType()
    {
        await SecondCommandTest();

        await Assert.ThrowsAnyAsync<Exception>(
            async () =>
            {
                commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
                    new AddItemToShoppingCartI
                        { CartId = cartId, Code = "TEST2", Name = "Name2", Quantity = 2 });
            });

    }

    [Fact]
    public async Task multiProjectionsTest()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.Command,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);

        var cartId1 = Guid.NewGuid();
        var cartId2 = Guid.NewGuid();
        var cartId3 = Guid.NewGuid();
        var cartId4 = Guid.NewGuid();

        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartI
                    { CartId = cartId1, Name = "Name1", Code = "Code1", Quantity = 1 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartI
                    { CartId = cartId1, Name = "Name2", Code = "Code2", Quantity = 2 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new SubmitOrderI
                    { CartId = cartId1, OrderSubmittedLocalTime = DateTime.Now, ReferenceVersion = commandResponse.Version })
            .Result;


        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartI
                    { CartId = cartId2, Name = "Name2", Code = "Code2", Quantity = 1 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new SubmitOrderI
                    { CartId = cartId2, OrderSubmittedLocalTime = DateTime.Now, ReferenceVersion = commandResponse.Version })
            .Result;

        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartI
                    { CartId = cartId3, Name = "Name3", Code = "Code3", Quantity = 1 })
            .Result;
        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartI
                    { CartId = cartId3, Name = "Name2", Code = "Code2", Quantity = 2 })
            .Result;

        commandResponse = commandExecutor.ExecCommandWithEventsAsync(
                new AddItemToShoppingCartI
                    { CartId = cartId4, Name = "Name4", Code = "Code4", Quantity = 1 })
            .Result;

        var list = await multiProjectionService.GetAggregateList<ICartAggregate>();
        Assert.Equal(4, list.Count);
        Assert.Equal(2, list.Count(m => m.Payload is ShoppingCartI));
        Assert.Equal(2, list.Count(m => m.Payload is PurchasedCartI));

    }
}
