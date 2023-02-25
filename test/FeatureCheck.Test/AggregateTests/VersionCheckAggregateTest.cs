using FeatureCheck.Domain.Aggregates.VersionCheckAggregates;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Projections;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class VersionCheckAggregateTest : AggregateTest<VersionCheckAggregate, FeatureCheckDependency>
{
    [Fact]
    private void VersionCheckAggregate_Create()
    {
        WhenCommand(
                new OldV1Command
                    { AggregateId = Guid.NewGuid(), Amount = 100 })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 100,
                    PaymentKind = PaymentKind.Cash,
                    Description = "Updated"
                })
            .ThenSingleProjectionPayloadIs(new VersionCheckAggregateLastInfo(100, PaymentKind.Cash, "Updated"));
    }
    [Fact]
    private void VersionCheckAggregate_V1Twice()
    {
        WhenCommand(
                new OldV1Command
                    { AggregateId = Guid.NewGuid(), Amount = 100 })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 100,
                    PaymentKind = PaymentKind.Cash,
                    Description = "Updated"
                })
            .WhenCommand(
                new OldV1Command
                    { AggregateId = GetAggregateId(), Amount = 200 })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 300,
                    PaymentKind = PaymentKind.Cash,
                    Description = "Updated"
                })
            .ThenSingleProjectionPayloadIs(new VersionCheckAggregateLastInfo(200, PaymentKind.Cash, "Updated"));
    }
    [Fact]
    private void VersionCheckAggregate_V2()
    {
        WhenCommand(
                new OldV2Command
                    { AggregateId = Guid.NewGuid(), Amount = 110, PaymentKind = PaymentKind.PayPal })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 110,
                    PaymentKind = PaymentKind.PayPal,
                    Description = "Updated"
                })
            .ThenSingleProjectionPayloadIs(new VersionCheckAggregateLastInfo(110, PaymentKind.PayPal, "Updated"));


    }
    [Fact]
    private void VersionCheckAggregate_V2Twice()
    {
        WhenCommand(
                new OldV2Command
                    { AggregateId = Guid.NewGuid(), Amount = 110, PaymentKind = PaymentKind.PayPal })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 110,
                    PaymentKind = PaymentKind.PayPal,
                    Description = "Updated"
                })
            .WhenCommand(
                new OldV2Command
                    { AggregateId = GetAggregateId(), Amount = 210, PaymentKind = PaymentKind.Cash })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 320,
                    PaymentKind = PaymentKind.Cash,
                    Description = "Updated"
                })
            .ThenSingleProjectionPayloadIs(new VersionCheckAggregateLastInfo(210, PaymentKind.Cash, "Updated"));

    }
    [Fact]
    private void VersionCheckAggregate_V3()
    {
        WhenCommand(
                new CurrentV3Command
                    { AggregateId = Guid.NewGuid(), Amount = 120, PaymentKind = PaymentKind.Other, Description = "using current event" })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 120,
                    PaymentKind = PaymentKind.Other,
                    Description = "using current event"
                })
            .ThenSingleProjectionPayloadIs(new VersionCheckAggregateLastInfo(120, PaymentKind.Other, "using current event"));


    }
    [Fact]
    private void VersionCheckAggregate_V3Twice()
    {
        WhenCommand(
                new CurrentV3Command
                    { AggregateId = Guid.NewGuid(), Amount = 120, PaymentKind = PaymentKind.Other, Description = "using current event" })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 120,
                    PaymentKind = PaymentKind.Other,
                    Description = "using current event"
                })
            .WhenCommand(
                new CurrentV3Command
                    { AggregateId = GetAggregateId(), Amount = 220, PaymentKind = PaymentKind.CreditCard, Description = "using current event 2" })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new VersionCheckAggregate
                {
                    Amount = 340,
                    PaymentKind = PaymentKind.CreditCard,
                    Description = "using current event 2"
                })
            .ThenSingleProjectionPayloadIs(new VersionCheckAggregateLastInfo(220, PaymentKind.CreditCard, "using current event 2"));
    }
}
