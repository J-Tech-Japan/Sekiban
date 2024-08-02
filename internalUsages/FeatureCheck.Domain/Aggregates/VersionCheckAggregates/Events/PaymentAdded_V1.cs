using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;

public record PaymentAdded_V1(int Amount) : IEventPayload<VersionCheckAggregate, PaymentAdded_V1>,
    IEventPayloadConvertingTo<PaymentAdded_V2>
{
    public static VersionCheckAggregate OnEvent(VersionCheckAggregate aggregatePayload, Event<PaymentAdded_V1> ev) =>
        throw new SekibanEventOutdatedException(typeof(PaymentAdded_V1));

    public PaymentAdded_V2 ConvertTo() => new(Amount, PaymentKind.Cash);
}
