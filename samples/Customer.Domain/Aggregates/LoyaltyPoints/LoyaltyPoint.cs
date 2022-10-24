using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Customer.Domain.Aggregates.LoyaltyPoints.ValueObjects;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints;

public class LoyaltyPoint : Aggregate<LoyaltyPointPayload>
{

    public void CreateLoyaltyPoint(int initialPoint)
    {
        AddAndApplyEvent(new LoyaltyPointCreated(initialPoint));
    }
    protected override Func<AggregateVariable<LoyaltyPointPayload>, AggregateVariable<LoyaltyPointPayload>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            LoyaltyPointCreated created => variable =>
                new AggregateVariable<LoyaltyPointPayload>(new LoyaltyPointPayload(created.InitialPoint, null)),
            LoyaltyPointAdded added => variable =>
                variable with { Contents = new LoyaltyPointPayload(Payload.CurrentPoint + added.PointAmount, added.HappenedDate) },
            LoyaltyPointUsed used => variable =>
                variable with { Contents = new LoyaltyPointPayload(Payload.CurrentPoint - used.PointAmount, used.HappenedDate) },
            LoyaltyPointDeleted => variable => variable with { IsDeleted = true },
            _ => null
        };
    }

    public void AddLoyaltyPoint(DateTime happenedDate, LoyaltyPointReceiveType reason, int pointAmount, string note)
    {
        if (Payload.LastOccuredTime is not null && Payload.LastOccuredTime > happenedDate)
        {
            throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
        }
        AddAndApplyEvent(new LoyaltyPointAdded(happenedDate, reason, pointAmount, note));
    }

    public void UseLoyaltyPoint(DateTime happenedDate, LoyaltyPointUsageType reason, int pointAmount, string note)
    {
        if (Payload.LastOccuredTime > happenedDate)
        {
            throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
        }
        if (Payload.CurrentPoint - pointAmount < 0)
        {
            throw new SekibanLoyaltyPointNotEnoughException();
        }
        AddAndApplyEvent(new LoyaltyPointUsed(happenedDate, reason, pointAmount, note));
    }

    public void Delete()
    {
        AddAndApplyEvent(new LoyaltyPointDeleted());
    }
}
