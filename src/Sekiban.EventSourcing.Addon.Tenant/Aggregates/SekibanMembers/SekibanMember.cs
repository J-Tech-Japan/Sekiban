using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.Events;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.ValueObjects;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers;

public class SekibanMember : TransferableAggregateBase<SekibanMemberContents>
{
    public void Create(SekibanMemberString name, SekibanMemberEmailString email, SekibanMemberUniqueIdString uniqueId)
    {
        AddAndApplyEvent(new SekibanMemberCreated(name, email, uniqueId));
    }
    protected override Func<AggregateVariable<SekibanMemberContents>, AggregateVariable<SekibanMemberContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            SekibanMemberCreated created => variable => new AggregateVariable<SekibanMemberContents>(
                new SekibanMemberContents(created.Name, created.Email, created.UniqueId)),
            _ => null
        };
    }
}
