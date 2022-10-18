using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.Events;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;

public class SekibanTenant : AggregateBase<SekibanTenantContents>
{
    public void CreateSekibanTenant(TenantNameString tenantName, TenantCodeString tenantCode)
    {
        AddAndApplyEvent(new SekibanTenantCreated(tenantName, tenantCode));
    }
    protected override Func<AggregateVariable<SekibanTenantContents>, AggregateVariable<SekibanTenantContents>>? GetApplyEventFunc(
        IAggregateEvent ev,
        IEventPayload payload)
    {
        return payload switch
        {
            SekibanTenantCreated created => _ => new AggregateVariable<SekibanTenantContents>(new SekibanTenantContents(created.Name, created.Code)),
            _ => null
        };
    }
}
