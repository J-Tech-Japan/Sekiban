using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;

public record PaymentAdded_V3(int Amount, PaymentKind PaymentKind, string Description) : IEventPayload<VersionCheckAggregate, PaymentAdded_V3>
{
    public static VersionCheckAggregate OnEvent(VersionCheckAggregate payload, Event<PaymentAdded_V3> ev) => payload with
    {
        Amount = payload.Amount + ev.Payload.Amount, PaymentKind = ev.Payload.PaymentKind, Description = ev.Payload.Description
    };
    public VersionCheckAggregate OnEventInstance(VersionCheckAggregate payload, Event<PaymentAdded_V3> ev) => OnEvent(payload, ev);
}
