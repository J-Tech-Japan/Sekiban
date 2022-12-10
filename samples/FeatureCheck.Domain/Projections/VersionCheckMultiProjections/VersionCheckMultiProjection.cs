using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultiProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.VersionCheckMultiProjections;

public record VersionCheckMultiProjection
    (ImmutableList<VersionCheckMultiProjection.Record> History) : IMultiProjectionPayload<VersionCheckMultiProjection>
{
    public VersionCheckMultiProjection() : this(ImmutableList<Record>.Empty) { }
    public Func<VersionCheckMultiProjection, VersionCheckMultiProjection>? GetApplyEventFunc(IEvent ev, IEventPayloadCommon eventPayload) =>
        eventPayload switch
        {
            PaymentAdded_V3 paymentAddedV3 => projection => new VersionCheckMultiProjection(
                projection.History.Add(new Record(paymentAddedV3.Amount, paymentAddedV3.PaymentKind, paymentAddedV3.Description))),
            _ => null
        };

    public record Record(int Amount, PaymentKind PaymentKind, string Description);
}
