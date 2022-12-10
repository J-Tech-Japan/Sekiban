using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Projections.VersionCheckMultiProjections;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class VersionCheckUnifiedTest : UnifiedTest<CustomerDependency>
{

    [Fact]
    public void V1Test()
    {
        var aggregateId = Guid.NewGuid();
        RunCommand(
            new OldV1Command
                { AggregateId = aggregateId, Amount = 100 });
        RunCommand(
            new OldV2Command
                { AggregateId = aggregateId, Amount = 200, PaymentKind = PaymentKind.CreditCard });
        RunCommand(
            new CurrentV3Command
                { AggregateId = aggregateId, Amount = 300, PaymentKind = PaymentKind.PayPal, Description = "Test" });
        ThenMultiProjectionPayloadIs(
            new VersionCheckMultiProjection(
                new List<VersionCheckMultiProjection.Record>()
                {
                    new (100, PaymentKind.Cash, "Updated"),
                    new (200, PaymentKind.CreditCard, "Updated"),
                    new (300, PaymentKind.PayPal, "Test")
                }.ToImmutableList()
            ));
    }
}
