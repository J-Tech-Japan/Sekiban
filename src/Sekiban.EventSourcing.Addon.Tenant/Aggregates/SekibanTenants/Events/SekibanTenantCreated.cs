using Sekiban.EventSourcing.AggregateEvents;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.Events
{
    public record SekibanTenantCreated(string Name, string Code) : ICreatedEventPayload;
}
