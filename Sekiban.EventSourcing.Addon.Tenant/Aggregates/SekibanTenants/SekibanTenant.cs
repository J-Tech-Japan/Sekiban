using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.Events;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;
using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;

public class SekibanTenant : TransferableAggregateBase<SekibanTenantContents>
{
    public void CreateSekibanTenant(NameString tenantName, TenantCodeString tenantCode) =>
        AddAndApplyEvent(new SekibanTenantCreated(tenantName, tenantCode));

    protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload) =>
        payload switch
        {
            SekibanTenantCreated created => () => Contents = new SekibanTenantContents(created.Name, created.Code),
            _ => null
        };
}
