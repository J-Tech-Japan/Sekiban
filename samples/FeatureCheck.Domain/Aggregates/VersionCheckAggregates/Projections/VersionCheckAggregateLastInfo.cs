using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Projections;

public record VersionCheckAggregateLastInfo
    (int LastAmount, PaymentKind LastPaymentKind, string LastDescription) : ISingleProjectionPayload<VersionCheckAggregate,
        VersionCheckAggregateLastInfo>
{
    public VersionCheckAggregateLastInfo() : this(0, PaymentKind.Cash, string.Empty)
    {
    }


    public static Func<VersionCheckAggregateLastInfo, VersionCheckAggregateLastInfo>? GetApplyEventFunc<TEventPayload>(Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon =>
        ev.Payload switch
        {
            PaymentAdded_V3 paymentAdded => payload => payload with
            {
                LastAmount = paymentAdded.Amount, LastPaymentKind = paymentAdded.PaymentKind, LastDescription = paymentAdded.Description
            },
            _ => null
        };
    public Func<VersionCheckAggregateLastInfo, VersionCheckAggregateLastInfo>?
        GetApplyEventFuncInstance<TEventPayload>(Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon =>
        GetApplyEventFunc(ev);
}
