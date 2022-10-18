using Sekiban.Addon.Tenant.Aggregates.SekibanMembers.Events;
using Sekiban.Addon.Tenant.Aggregates.SekibanMembers.ValueObjects;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanMembers;

public class SekibanMember : AggregateBase<SekibanMemberContents>
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
