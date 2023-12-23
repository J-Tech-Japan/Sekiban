using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace FeatureCheck.Test.AggregateTests.Subtypes;

public class CartAggregateRTest : AggregateTest<CartAggregateR, FeatureCheckDependency>
{
    private readonly Guid CartId = Guid.NewGuid();
    [Fact]
    public void CommandExecuteTest()
    {
        Subtype<ShoppingCartR>()
            .WhenCommand(new AddItemToShoppingCartR { CartId = CartId, Code = "TESTCODE", Name = "TESTNAME", Quantity = 100 })
            .ThenGetLatestEvents(
                events =>
                {
                    var ev = events.First();
                    Assert.Equal(nameof(CartAggregateR), ev.AggregateType);
                })
            .ThenPayloadTypeShouldBe<ShoppingCartR>()
            .ThenPayloadIs(
                new ShoppingCartR
                {
                    Items = ImmutableSortedDictionary<int, CartItemRecordR>.Empty.Add(
                        0,
                        new CartItemRecordR { Code = "TESTCODE", Name = "TESTNAME", Quantity = 100 })
                });
    }
    [Fact]
    public void CommandExecuteTestAndChangeAggregateType()
    {
        Subtype<ShoppingCartR>()
            .WhenCommand(new AddItemToShoppingCartR { CartId = CartId, Code = "TESTCODE", Name = "TESTNAME", Quantity = 100 })
            .ThenPayloadTypeShouldBe<ShoppingCartR>()
            .WhenCommand(
                new SubmitOrderR
                {
                    CartId = CartId,
                    OrderSubmittedLocalTime = new DateTime(
                        2023,
                        2,
                        2,
                        2,
                        22,
                        2)
                })
            .ThenPayloadTypeShouldBe<PurchasedCartR>()
            .ThenPayloadIs(
                new PurchasedCartR
                {
                    Items = ImmutableSortedDictionary<int, CartItemRecordR>.Empty.Add(
                        0,
                        new CartItemRecordR { Code = "TESTCODE", Name = "TESTNAME", Quantity = 100 }),
                    PurchasedDate = new DateTime(
                        2023,
                        2,
                        2,
                        2,
                        22,
                        2)
                });
    }
}
