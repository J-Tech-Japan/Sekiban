using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;

public record PaymentAdded_V3(int Amount, PaymentKind PaymentKind, string Description) : IEventPayload<VersionCheckAggregate>
{
    public VersionCheckAggregate OnEvent(VersionCheckAggregate payload, IEvent ev) => payload with
    {
        Amount = payload.Amount + Amount, PaymentKind = PaymentKind, Description = Description
    };
}
