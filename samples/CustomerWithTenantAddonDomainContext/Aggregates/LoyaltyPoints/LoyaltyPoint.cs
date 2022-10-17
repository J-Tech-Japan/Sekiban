using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.ValueObjects;
using CustomerWithTenantAddonDomainContext.Shared.Exceptions;
namespace CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints;

public class LoyaltyPoint : TransferableAggregateBase<LoyaltyPointContents>
{

    public void CreateLoyaltyPoint(int initialPoint)
    {
        AddAndApplyEvent(new LoyaltyPointCreated(initialPoint));
    }

    // protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload)
    // {
    //     return payload switch
    //     {
    //         LoyaltyPointCreated created => () =>
    //         {
    //             Contents = new LoyaltyPointContents(created.InitialPoint, null);
    //         },
    //         LoyaltyPointAdded added => () =>
    //         {
    //             Contents = new LoyaltyPointContents(Contents.CurrentPoint + added.PointAmount, added.HappenedDate);
    //         },
    //         LoyaltyPointUsed used => () =>
    //         {
    //             Contents = new LoyaltyPointContents(Contents.CurrentPoint - used.PointAmount, used.HappenedDate);
    //         },
    //         LoyaltyPointDeleted => () =>
    //         {
    //             IsDeleted = true;
    //         },
    //         _ => null
    //     };
    // }
    protected override Func<AggregateVariable<LoyaltyPointContents>, AggregateVariable<LoyaltyPointContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            LoyaltyPointCreated created => _ => new AggregateVariable<LoyaltyPointContents>(new LoyaltyPointContents(created.InitialPoint, null)),
            LoyaltyPointAdded added => variable =>
                variable with { Contents = new LoyaltyPointContents(Contents.CurrentPoint + added.PointAmount, added.HappenedDate) },
            LoyaltyPointUsed used => variable =>
                variable with { Contents = new LoyaltyPointContents(Contents.CurrentPoint - used.PointAmount, used.HappenedDate) },
            LoyaltyPointDeleted => variable => variable with { IsDeleted = true },
            _ => null
        };
    }

    public void AddLoyaltyPoint(DateTime happenedDate, LoyaltyPointReceiveType reason, int pointAmount, string note)
    {
        if (Contents.LastOccuredTime is not null && Contents.LastOccuredTime > happenedDate)
        {
            throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
        }
        AddAndApplyEvent(new LoyaltyPointAdded(happenedDate, reason, pointAmount, note));
    }

    public void UseLoyaltyPoint(DateTime happenedDate, LoyaltyPointUsageType reason, int pointAmount, string note)
    {
        if (Contents.LastOccuredTime > happenedDate)
        {
            throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
        }
        if (Contents.CurrentPoint - pointAmount < 0)
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
