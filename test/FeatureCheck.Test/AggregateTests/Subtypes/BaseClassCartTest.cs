using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace FeatureCheck.Test.AggregateTests.Subtypes;

public class BaseClassCartTest : AggregateTest<ICartAggregate, FeatureCheckDependency>
{
    private readonly Guid CartId = Guid.NewGuid();
    [Fact]
    public void CommandExecuteTest()
    {
        Subtype<ShoppingCartI>()
            .WhenCommand(
                new AddItemToShoppingCartI
                {
                    CartId = CartId, Code = "TESTCODE", Name = "TESTNAME", Quantity = 100
                })
            .ThenGetLatestEvents(
                events =>
                {
                    var ev = events.First();
                    Assert.Equal(nameof(ICartAggregate), ev.AggregateType);
                })
            .ThenPayloadTypeShouldBe<ShoppingCartI>()
            .ThenPayloadIs(
                new ShoppingCartI
                {
                    Items = ImmutableSortedDictionary<int, CartItemRecordI>.Empty.Add(
                        0,
                        new CartItemRecordI
                            { Code = "TESTCODE", Name = "TESTNAME", Quantity = 100 })
                });
    }
    [Fact]
    public void CommandExecuteTestAndChangeAggregateType()
    {
        Subtype<ShoppingCartI>()
            .WhenCommand(
                new AddItemToShoppingCartI
                {
                    CartId = CartId, Code = "TESTCODE", Name = "TESTNAME", Quantity = 100
                })
            .ThenPayloadTypeShouldBe<ShoppingCartI>()
            .WhenCommand(
                new SubmitOrderI
                {
                    CartId = CartId, OrderSubmittedLocalTime = new DateTime(
                        2023,
                        2,
                        2,
                        2,
                        22,
                        2)
                })
            .ThenPayloadTypeShouldBe<PurchasedCartI>()
            .ThenPayloadIs(
                new PurchasedCartI
                {
                    Items = ImmutableSortedDictionary<int, CartItemRecordI>.Empty.Add(
                        0,
                        new CartItemRecordI
                        {
                            Code = "TESTCODE", Name = "TESTNAME", Quantity = 100
                        }),
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