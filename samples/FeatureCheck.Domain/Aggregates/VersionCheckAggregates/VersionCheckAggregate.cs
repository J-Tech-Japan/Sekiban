using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates;

public record VersionCheckAggregate : IAggregatePayload<VersionCheckAggregate>
{
    public int Amount { get; init; }
    public PaymentKind PaymentKind { get; init; } = PaymentKind.Other;
    public string Description { get; init; } = string.Empty;
    public static VersionCheckAggregate CreateInitialPayload(VersionCheckAggregate? _) => new();
}
