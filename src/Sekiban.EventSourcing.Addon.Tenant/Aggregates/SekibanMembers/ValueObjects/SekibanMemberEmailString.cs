using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.ValueObjects;

public record SekibanMemberEmailString : SingleValueObjectNoValidationClassBase<string>
{

    public SekibanMemberEmailString(string value) : base(value) { }
    public static implicit operator string(SekibanMemberEmailString vo) =>
        vo.Value;
    public static implicit operator SekibanMemberEmailString(string v) =>
        new(v);
}