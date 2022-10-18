using Sekiban.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanMembers.ValueObjects;

public record SekibanMemberUniqueIdString : SingleValueObjectNoValidationClassBase<string>
{

    public SekibanMemberUniqueIdString(string value) : base(value) { }
    public static implicit operator string(SekibanMemberUniqueIdString vo)
    {
        return vo.Value;
    }
    public static implicit operator SekibanMemberUniqueIdString(string v)
    {
        return new(v);
    }
}
