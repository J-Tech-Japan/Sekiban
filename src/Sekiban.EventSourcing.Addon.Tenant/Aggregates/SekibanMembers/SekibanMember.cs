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
    protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload) =>
        payload switch
        {
            SekibanMemberCreated created => () => Contents = new SekibanMemberContents(created.Name, created.Email, created.UniqueId),
            _ => null
        };
}