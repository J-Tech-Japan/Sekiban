using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Projections;

public record VersionCheckAggregateLastInfo
    (int LastAmount, PaymentKind LastPaymentKind, string LastDescription) : ISingleProjectionPayload<VersionCheckAggregate,
        VersionCheckAggregateLastInfo>
{
    public static VersionCheckAggregateLastInfo? ApplyEvent<TEventPayload>(VersionCheckAggregateLastInfo projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon =>
        ev.Payload switch
        {
            PaymentAdded_V3 paymentAdded => new VersionCheckAggregateLastInfo(
                paymentAdded.Amount,
                paymentAdded.PaymentKind,
                paymentAdded.Description),
            _ => null
        };
    public static IAggregatePayloadCommon CreateInitialPayload() => new VersionCheckAggregateLastInfo(0, PaymentKind.Cash, string.Empty);
}
