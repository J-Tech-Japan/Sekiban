using CustomerDomainContext.Aggregates.LoyaltyPoints.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints.ValueObjects;
using CustomerDomainContext.Shared.Exceptions;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints;

public class LoyaltyPoint : TransferableAggregateBase<LoyaltyPointDto>
{
    private int CurrentPoint { get; set; }
    private DateTime LastOccuredTime { get; set; }

    public LoyaltyPoint(Guid aggregateId) : base(aggregateId) { }

    public LoyaltyPoint(Guid clientId, int initialPoint) : base(clientId)
    {
        AddAndApplyEvent(new LoyaltyPointCreated(clientId, initialPoint));
    }

    public override LoyaltyPointDto ToDto() =>
        new(this) { CurrentPoint = CurrentPoint };

    protected override void CopyPropertiesFromSnapshot(LoyaltyPointDto snapshot)
    {
        CurrentPoint = snapshot.CurrentPoint;
    }

    protected override Action? GetApplyEventAction(AggregateEvent ev) =>
        ev switch
        {
            LoyaltyPointCreated created => () =>
            {
                CurrentPoint = created.InitialPoint;
            },
            LoyaltyPointAdded added => () =>
            {
                CurrentPoint += added.PointAmount;
                LastOccuredTime = added.HappenedDate;
            },
            LoyaltyPointUsed used => () =>
            {
                CurrentPoint -= used.PointAmount;
                LastOccuredTime = used.HappenedDate;
            },
            LoyaltyPointDeleted => () =>
            {
                IsDeleted = true;
            },
            _ => null
        };

    public void AddLoyaltyPoint(DateTime happenedDate, LoyaltyPointReceiveType reason, int pointAmount, string note)
    {
        if (LastOccuredTime > happenedDate)
        {
            throw new JJLoyaltyPointCanNotHappenOnThisTimeException();
        }
        AddAndApplyEvent(new LoyaltyPointAdded(AggregateId, happenedDate, reason, pointAmount, note));
    }

    public void UseLoyaltyPoint(DateTime happenedDate, LoyaltyPointUsageType reason, int pointAmount, string note)
    {
        if (LastOccuredTime > happenedDate)
        {
            throw new JJLoyaltyPointCanNotHappenOnThisTimeException();
        }
        if (CurrentPoint - pointAmount < 0)
        {
            throw new JJLoyaltyPointNotEnoughException();
        }
        AddAndApplyEvent(new LoyaltyPointUsed(AggregateId, happenedDate, reason, pointAmount, note));
    }

    public void Delete() =>
        AddAndApplyEvent(new LoyaltyPointDeleted(AggregateId));
}
