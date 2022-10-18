using Sekiban.Core.Event;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanMembers.Events;

public record SekibanMemberCreated(string Name, string Email, string UniqueId) : ICreatedEventPayload;
