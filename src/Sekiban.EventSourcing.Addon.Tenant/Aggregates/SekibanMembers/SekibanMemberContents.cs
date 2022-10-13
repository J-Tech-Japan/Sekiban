using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers;

public record SekibanMemberContents(string Name, string Email, string UniqueIdentifier) : IAggregateContents
{
    public SekibanMemberContents() : this(string.Empty, string.Empty, string.Empty) { }
}
