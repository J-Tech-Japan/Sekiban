using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Projections;

public record VersionCheckAggregateLastInfo(int LastAmount, PaymentKind LastPaymentKind, string LastDescription)
    : ISingleProjectionPayload<VersionCheckAggregate,
        VersionCheckAggregateLastInfo>
{
    public static VersionCheckAggregateLastInfo? ApplyEvent<TEventPayload>(
        VersionCheckAggregateLastInfo projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon
    {
        return ev.Payload switch
        {
            PaymentAdded_V3 paymentAdded => new VersionCheckAggregateLastInfo(
                paymentAdded.Amount,
                paymentAdded.PaymentKind,
                paymentAdded.Description),
            _ => null
        };
    }

    public static VersionCheckAggregateLastInfo CreateInitialPayload() => new(0, PaymentKind.Cash, string.Empty);
}
