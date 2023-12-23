using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.VersionCheckMultiProjections;

public record VersionCheckMultiProjection
    (ImmutableList<VersionCheckMultiProjection.Record> History) : IMultiProjectionPayload<VersionCheckMultiProjection>
{
    public static VersionCheckMultiProjection? ApplyEvent<TEventPayload>(VersionCheckMultiProjection projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon =>
        ev.Payload switch
        {
            PaymentAdded_V3 paymentAddedV3 => new VersionCheckMultiProjection(
                projectionPayload.History.Add(new Record(paymentAddedV3.Amount, paymentAddedV3.PaymentKind, paymentAddedV3.Description))),
            _ => null
        };
    public static VersionCheckMultiProjection CreateInitialPayload() => new(ImmutableList<Record>.Empty);

    public record Record(int Amount, PaymentKind PaymentKind, string Description);
}
