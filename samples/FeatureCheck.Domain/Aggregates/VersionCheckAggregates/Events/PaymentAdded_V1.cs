using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;

public record PaymentAdded_V1(int Amount) : IEventPayload<VersionCheckAggregate>, IEventPayloadConvertingTo<PaymentAdded_V2>
{
    public VersionCheckAggregate OnEvent(VersionCheckAggregate payload, IEvent ev) => throw new SekibanEventOutdatedException(GetType());
    public PaymentAdded_V2 ConvertTo() => new(Amount, PaymentKind.Cash);
}
