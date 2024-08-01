using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.TenantUsers;

public record TenantUserCreated(string Name, string Email) : IEventPayload<TenantUser, TenantUserCreated>
{
    public static TenantUser OnEvent(TenantUser aggregatePayload, Event<TenantUserCreated> ev) => aggregatePayload with
    {
        Name = ev.Payload.Name, Email = ev.Payload.Email
    };
}
