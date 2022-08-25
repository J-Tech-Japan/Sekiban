using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.ValueObjects;

public record SekibanMemberUniqueIdString : SingleValueObjectNoValidationClassBase<string>
{

    public SekibanMemberUniqueIdString(string value) : base(value) { }
    public static implicit operator string(SekibanMemberUniqueIdString vo) =>
        vo.Value;
    public static implicit operator SekibanMemberUniqueIdString(string v) =>
        new(v);
}
