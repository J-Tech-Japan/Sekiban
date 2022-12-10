using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;

public record PaymentAdded_V2(int Amount, PaymentKind PaymentKind) : IEventPayload<VersionCheckAggregate>, IEventPayloadConvertingTo<PaymentAdded_V3>
{
    public VersionCheckAggregate OnEvent(VersionCheckAggregate payload, IEvent ev) => throw new SekibanEventOutdatedException(GetType());
    public PaymentAdded_V3 ConvertTo() => new(Amount, PaymentKind, "Updated");
}
