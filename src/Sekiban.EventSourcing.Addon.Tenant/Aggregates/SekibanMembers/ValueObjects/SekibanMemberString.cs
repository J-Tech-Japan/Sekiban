using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.ValueObjects;

public record SekibanMemberString : SingleValueObjectNoValidationClassBase<string>
{

    public SekibanMemberString(string value) : base(value) { }
    public static implicit operator string(SekibanMemberString vo)
    {
        return vo.Value;
    }
    public static implicit operator SekibanMemberString(string v)
    {
        return new(v);
    }
}
