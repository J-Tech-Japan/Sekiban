using Sekiban.Core.Event;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanTenants.Events;

public record SekibanTenantCreated(string Name, string Code) : ICreatedEventPayload;
