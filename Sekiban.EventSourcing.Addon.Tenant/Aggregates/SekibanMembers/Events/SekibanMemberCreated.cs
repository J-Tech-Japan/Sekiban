using Sekiban.EventSourcing.AggregateEvents;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.Events;

public record SekibanMemberCreated(string Name, string Email, string UniqueId) : ICreatedEventPayload;
