using Sekiban.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanMembers.ValueObjects;

public record SekibanMemberEmailString : SingleValueObjectNoValidationClassBase<string>
{

    public SekibanMemberEmailString(string value) : base(value) { }
    public static implicit operator string(SekibanMemberEmailString vo)
    {
        return vo.Value;
    }
    public static implicit operator SekibanMemberEmailString(string v)
    {
        return new(v);
    }
}
