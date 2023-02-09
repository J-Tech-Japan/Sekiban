using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts.Commands;
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
        Subtype<ShoppingCartI>(
                subType => subType.WhenCommand(
                        new AddItemToShoppingCartI
                        {
                            CartId = CartId, Code = "TESTCODE", Name = "TESTNAME", Quantity = 100
                        })
                    .ThenGetLatestEvents(
                        events =>
                        {
                            var ev = events.First();
                            Assert.Equal(nameof(ICartAggregate), ev.AggregateType);
                        }))
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
        // Subtype<ShoppingCartI>()
        //     .WhenCommand(
        //         new AddItemToShoppingCartI
        //         {
        //             CartId = CartId, Code = "TESTCODE", Name = "TESTNAME", Quantity = 100
        //         })
        //     .ThenPayloadTypeIs<ShoppingCartI>();
        //     .WhenCommand(
        //     new SubmitOrderI
        //     {
        //         CartId = CartId, OrderSubmittedLocalTime = new DateTime(
        //             2023,
        //             2,
        //             2,
        //             2,
        //             22,
        //             2)
        //     }));
        //
        // Subtype<PurchasedCartI>()
        //     .WhenCommand(new PurchaseCartI { CartId = CartId })
        //


        Subtype<ShoppingCartI>(
                subType => subType.WhenCommand(
                        new AddItemToShoppingCartI
                        {
                            CartId = CartId, Code = "TESTCODE", Name = "TESTNAME", Quantity = 100
                        })
                    .ThenPayloadTypeIs<ShoppingCartI>()
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
                    .ThenPayloadTypeIs<PurchasedCartI>()
                    .ThenPayloadIs()
            )
            .ThenPayloadTypeIs<PurchasedCartI>()
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
