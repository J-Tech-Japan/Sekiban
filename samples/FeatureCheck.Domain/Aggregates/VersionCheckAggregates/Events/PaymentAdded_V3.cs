using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;

public record PaymentAdded_V3(int Amount, PaymentKind PaymentKind, string Description) : IEventPayload<VersionCheckAggregate, PaymentAdded_V3>
{
    public VersionCheckAggregate OnEventInstance(VersionCheckAggregate aggregatePayload, Event<PaymentAdded_V3> ev) => OnEvent(aggregatePayload, ev);
    public static VersionCheckAggregate OnEvent(VersionCheckAggregate aggregatePayload, Event<PaymentAdded_V3> ev) =>
        aggregatePayload with
        {
            Amount = aggregatePayload.Amount + ev.Payload.Amount, PaymentKind = ev.Payload.PaymentKind, Description = ev.Payload.Description
        };
}
