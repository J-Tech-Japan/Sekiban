using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;

public record PaymentAdded_V2(int Amount, PaymentKind PaymentKind) : IEventPayload<VersionCheckAggregate, PaymentAdded_V2>,
    IEventPayloadConvertingTo<PaymentAdded_V3>
{
    public VersionCheckAggregate OnEventInstance(VersionCheckAggregate aggregatePayload, Event<PaymentAdded_V2> ev) => OnEvent(aggregatePayload, ev);
    public PaymentAdded_V3 ConvertTo() => new(Amount, PaymentKind, "Updated");
    public static VersionCheckAggregate OnEvent(VersionCheckAggregate aggregatePayload, Event<PaymentAdded_V2> ev) =>
        throw new SekibanEventOutdatedException(typeof(PaymentAdded_V2));
}
