using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates;

public record VersionCheckAggregate : IAggregatePayload
{
    public int Amount { get; init; }
    public PaymentKind PaymentKind { get; init; } = PaymentKind.Other;
    public string Description { get; init; } = string.Empty;
}
